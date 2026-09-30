using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NexJob.Configuration;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Tests for issue #261: recurring jobs declared in configuration are applied on every start, keep what an operator
/// changed in the dashboard, and first run at the next cron occurrence instead of immediately.
/// </summary>
public sealed class RecurringJobRegistrarRestartTests
{
    private readonly InMemoryStorageProvider _storage = new();
    private readonly NexJobJobRegistry _jobRegistry = new();

    public RecurringJobRegistrarRestartTests()
    {
        _jobRegistry.Register(typeof(RecurringJobRegistrarHardeningTests.TestJob));
    }

    /// <summary>N1 (Positive): a cron changed in configuration is applied when the application restarts.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ConfigCronChanged_OnRestart_StoredCronIsUpdated()
    {
        await NewRegistrar().RegisterRecurringJobsAsync([Config("0 0 * * *")]);

        await NewRegistrar().RegisterRecurringJobsAsync([Config("0 12 * * *")]);

        var stored = await _storage.GetRecurringJobByIdAsync(JobName);
        stored!.Cron.Should().Be("0 12 * * *");
        var expected = DefaultScheduler.ParseCron("0 12 * * *").GetNextOccurrence(DateTimeOffset.UtcNow, TimeZoneInfo.Utc);
        stored.NextExecution.Should().BeCloseTo(expected!.Value, TimeSpan.FromSeconds(2));
    }

    /// <summary>N2 (Negative): a cron override, a pause and a delete made in the dashboard survive a restart.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task DashboardOverrideAndDelete_SurviveRestart()
    {
        await NewRegistrar().RegisterRecurringJobsAsync([Config("0 0 * * *")]);
        await _storage.UpdateRecurringJobConfigAsync(JobName, "*/5 * * * *", enabled: false);
        (await _storage.GetRecurringJobByIdAsync(JobName))!.DeletedByUser = true;

        await NewRegistrar().RegisterRecurringJobsAsync([Config("0 12 * * *")]);

        var stored = await _storage.GetRecurringJobByIdAsync(JobName);
        stored!.CronOverride.Should().Be("*/5 * * * *");
        stored.Enabled.Should().BeFalse();
        stored.DeletedByUser.Should().BeTrue();
    }

    /// <summary>N3 (Boundary): a job registered for the first time waits for its next cron occurrence.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task NewJob_NextExecutionIsNextCronOccurrence_NotInThePast()
    {
        var before = DateTimeOffset.UtcNow;

        await NewRegistrar().RegisterRecurringJobsAsync([Config("0 0 1 1 *")]);

        var stored = await _storage.GetRecurringJobByIdAsync(JobName);
        stored!.NextExecution.Should().BeAfter(before, "a fresh registration must not fire immediately");
    }

    /// <summary>N3 (Invalid input): an unknown time zone fails the registration and stores nothing.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task InvalidTimeZone_IsNotRegistered()
    {
        var registrar = NewRegistrar();
        var config = Config("0 0 * * *");
        config.TimeZoneId = "Not/AZone";

        await registrar.RegisterRecurringJobsAsync([config]);

        registrar.RegisteredJobIds.Should().BeEmpty();
        (await _storage.GetRecurringJobByIdAsync(JobName)).Should().BeNull();
    }

    private static string JobName => nameof(RecurringJobRegistrarHardeningTests.TestJob);

    private static RecurringJobSettings Config(string cron) => new() { Job = JobName, Cron = cron };

    private RecurringJobRegistrar NewRegistrar() =>
        new(_storage, _jobRegistry, NullLogger<RecurringJobRegistrar>.Instance);
}
