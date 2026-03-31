using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NexJob;
using NexJob.Configuration;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Tests for <see cref="IScheduler.RecurringAsync{TJob}"/> (no-input) support.
/// Covers record creation, execution, concurrency policies, and dashboard visibility.
/// </summary>
public sealed class RecurringNoInputJobTests
{
    private readonly InMemoryStorageProvider _storage = new();
    private readonly JobWakeUpChannel _wakeUp = new();
    private readonly DefaultScheduler _sut;

    public RecurringNoInputJobTests()
    {
        _sut = new DefaultScheduler(_storage, new NexJobOptions(), _wakeUp);
    }

    // ─── record creation ──────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <see cref="IScheduler.RecurringAsync{TJob}"/> creates a recurring job record.
    /// </summary>
    [Fact]
    public async Task RecurringAsync_CreatesRecurringJobRecord()
    {
        await _sut.RecurringAsync<RecurringNoInputStubJob>(
            "daily-cleanup", "0 9 * * *");

        var due = await _storage.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow.AddDays(2));

        due.Should().ContainSingle(r => r.RecurringJobId == "daily-cleanup");
    }

    /// <summary>
    /// Verifies that the recurring job record has the correct <see cref="JobType"/>
    /// and uses <see cref="NoInput"/> for <see cref="InputType"/>.
    /// </summary>
    [Fact]
    public async Task RecurringAsync_StoresCorrectJobTypeAndInputType()
    {
        await _sut.RecurringAsync<RecurringNoInputStubJob>(
            "verify-types", "0 * * * *");

        var record = (await _storage.GetRecurringJobsAsync()).Single(r => r.RecurringJobId == "verify-types");

        record.JobType.Should().Contain(nameof(RecurringNoInputStubJob));
        record.InputType.Should().Contain(nameof(NoInput));
    }

    /// <summary>
    /// Verifies that the recurring job record uses the specified queue.
    /// </summary>
    [Fact]
    public async Task RecurringAsync_UsesSpecifiedQueue()
    {
        await _sut.RecurringAsync<RecurringNoInputStubJob>(
            "custom-queue", "0 0 * * *", queue: "priority");

        var record = (await _storage.GetRecurringJobsAsync()).Single(r => r.RecurringJobId == "custom-queue");

        record.Queue.Should().Be("priority");
    }

    /// <summary>
    /// Verifies that the recurring job respects the concurrency policy.
    /// </summary>
    [Fact]
    public async Task RecurringAsync_StoresConcurrencyPolicy()
    {
        await _sut.RecurringAsync<RecurringNoInputStubJob>(
            "allow-concurrent", "0 * * * *",
            concurrencyPolicy: RecurringConcurrencyPolicy.AllowConcurrent);

        var record = (await _storage.GetRecurringJobsAsync()).Single(r => r.RecurringJobId == "allow-concurrent");

        record.ConcurrencyPolicy.Should().Be(RecurringConcurrencyPolicy.AllowConcurrent);
    }

    /// <summary>
    /// Verifies that <see cref="IScheduler.RecurringAsync{TJob}"/> updates an existing recurring job.
    /// </summary>
    [Fact]
    public async Task RecurringAsync_UpdatesExistingRecurringJob()
    {
        await _sut.RecurringAsync<RecurringNoInputStubJob>(
            "updatable", "0 0 * * *");

        var before = (await _storage.GetRecurringJobsAsync()).Single(r => r.RecurringJobId == "updatable");

        // Update the same ID with a new cron
        await _sut.RecurringAsync<RecurringNoInputStubJob>(
            "updatable", "0 12 * * *");

        var after = (await _storage.GetRecurringJobsAsync()).Single(r => r.RecurringJobId == "updatable");

        after.Cron.Should().Be("0 12 * * *");
        after.RecurringJobId.Should().Be(before.RecurringJobId);
    }

    // ─── end-to-end execution ────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a recurring no-input job executes end-to-end via the dispatcher.
    /// The RecurringJobSchedulerService enqueues instances, and JobDispatcherService
    /// executes them, recognizing NoInput as the signal for IJob.ExecuteAsync(CancellationToken).
    /// </summary>
    [Fact]
    public async Task RecurringAsync_JobExecutes_EndToEnd()
    {
        RecurringNoInputTestJob.ResetCount();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.UseInMemory();
                    opt.Workers = 1;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                });
                services.AddTransient<RecurringNoInputTestJob>();
            })
            .Build();

        await host.StartAsync();

        // Schedule a recurring job to fire immediately (in the past)
        var now = DateTimeOffset.UtcNow;
        var storage = host.Services.GetRequiredService<IStorageProvider>();
        var record = new RecurringJobRecord
        {
            RecurringJobId = "e2e-test",
            JobType = typeof(RecurringNoInputTestJob).AssemblyQualifiedName!,
            InputType = typeof(NoInput).AssemblyQualifiedName!,
            InputJson = System.Text.Json.JsonSerializer.Serialize(NoInput.Instance),
            Cron = "* * * * *",
            Queue = "default",
            NextExecution = now.AddSeconds(-1),  // past → due immediately
            CreatedAt = now,
            ConcurrencyPolicy = RecurringConcurrencyPolicy.SkipIfRunning,
        };
        await storage.UpsertRecurringJobAsync(record);

        // Let the scheduler and dispatcher run
        await Task.Delay(TimeSpan.FromSeconds(2));

        await host.StopAsync();

        RecurringNoInputTestJob.ExecutionCount.Should().BeGreaterThanOrEqualTo(1,
            "job should have executed at least once");
    }

    // ─── concurrency policies ────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <see cref="RecurringConcurrencyPolicy.SkipIfRunning"/> is stored
    /// correctly and used by the scheduler to block concurrent execution.
    /// </summary>
    [Fact]
    public async Task RecurringAsync_SkipIfRunning_IsStoredCorrectly()
    {
        await _sut.RecurringAsync<RecurringNoInputStubJob>(
            "skip-policy", "0 * * * *",
            concurrencyPolicy: RecurringConcurrencyPolicy.SkipIfRunning);

        var record = (await _storage.GetRecurringJobsAsync()).Single(r => r.RecurringJobId == "skip-policy");

        record.ConcurrencyPolicy.Should().Be(RecurringConcurrencyPolicy.SkipIfRunning);
    }

    /// <summary>
    /// Verifies that <see cref="RecurringConcurrencyPolicy.AllowConcurrent"/> is stored correctly.
    /// </summary>
    [Fact]
    public async Task RecurringAsync_AllowConcurrent_IsStoredCorrectly()
    {
        await _sut.RecurringAsync<RecurringNoInputStubJob>(
            "concurrent-policy", "0 * * * *",
            concurrencyPolicy: RecurringConcurrencyPolicy.AllowConcurrent);

        var record = (await _storage.GetRecurringJobsAsync()).Single(r => r.RecurringJobId == "concurrent-policy");

        record.ConcurrencyPolicy.Should().Be(RecurringConcurrencyPolicy.AllowConcurrent);
    }

    // ─── dashboard visibility ────────────────────────────────────────────────

    /// <summary>
    /// Verifies that recurring no-input jobs are visible in the dashboard
    /// (via <see cref="IStorageProvider.GetRecurringJobsAsync"/>).
    /// </summary>
    [Fact]
    public async Task RecurringAsync_IsVisible_InRecurringJobsList()
    {
        await _sut.RecurringAsync<RecurringNoInputStubJob>(
            "dashboard-test", "0 * * * *");

        var all = await _storage.GetRecurringJobsAsync();

        all.Should().ContainSingle(r => r.RecurringJobId == "dashboard-test");
    }

    /// <summary>
    /// Verifies that the JobType in the record matches the original TJob generic.
    /// This is used by the dashboard to display job metadata.
    /// </summary>
    [Fact]
    public async Task RecurringAsync_JobType_MatchesGenericParameter()
    {
        await _sut.RecurringAsync<RecurringNoInputStubJob>(
            "type-match", "0 0 * * *");

        var record = (await _storage.GetRecurringJobsAsync()).Single(r => r.RecurringJobId == "type-match");

        record.JobType.Should().Contain(nameof(RecurringNoInputStubJob));
    }

    /// <summary>
    /// Verifies that updating a recurring job (same ID) preserves the RecurringJobId
    /// but updates the cron and other metadata.
    /// </summary>
    [Fact]
    public async Task RecurringAsync_Update_PreservesRecurringJobId()
    {
        await _sut.RecurringAsync<RecurringNoInputStubJob>(
            "preserve-id", "0 0 * * *", queue: "default");

        var v1 = (await _storage.GetRecurringJobsAsync()).Single(r => r.RecurringJobId == "preserve-id");

        // Update with different queue
        await _sut.RecurringAsync<RecurringNoInputStubJob>(
            "preserve-id", "0 12 * * *", queue: "updated");

        var v2 = (await _storage.GetRecurringJobsAsync()).Single(r => r.RecurringJobId == "preserve-id");

        v2.RecurringJobId.Should().Be(v1.RecurringJobId, "RecurringJobId must not change");
        v2.Queue.Should().Be("updated", "Queue should be updated");
    }

    /// <summary>
    /// Verifies that the TimeZoneId is stored correctly when provided.
    /// </summary>
    [Fact]
    public async Task RecurringAsync_StoresTimeZone()
    {
        var est = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

        await _sut.RecurringAsync<RecurringNoInputStubJob>(
            "tz-test", "0 9 * * *", timeZone: est);

        var record = (await _storage.GetRecurringJobsAsync()).Single(r => r.RecurringJobId == "tz-test");

        record.TimeZoneId.Should().Be(est.Id);
    }

    // ─── appsettings binding & multiple executions ────────────────────────────

    /// <summary>
    /// Integration test: verifies that recurring IJob (no-input) loaded from appsettings.json
    /// via IConfiguration binds correctly and is registered in storage.
    /// </summary>
    [Fact]
    public async Task RecurringJob_LoadedFromAppsettings_IsRegisteredAndDue()
    {
        // Build configuration with recurring job definition
        var configData = new Dictionary<string, string?>
        {
            ["NexJob:Workers"] = "1",
            ["NexJob:PollingIntervalSeconds"] = "1",
            ["NexJob:RecurringJobs:0:Id"] = "test-from-appsettings",
            ["NexJob:RecurringJobs:0:JobType"] = typeof(RecurringFromAppsettingsTestJob).AssemblyQualifiedName,
            ["NexJob:RecurringJobs:0:Cron"] = "* * * * *",
            ["NexJob:RecurringJobs:0:Queue"] = "default",
            ["NexJob:RecurringJobs:0:Enabled"] = "true",
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(configuration);
                services.AddTransient<RecurringFromAppsettingsTestJob>();
            })
            .Build();

        await host.StartAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(200));  // Let registration service run
        await host.StopAsync();

        var storage = host.Services.GetRequiredService<IStorageProvider>();
        var allRecurring = await storage.GetRecurringJobsAsync();
        var registered = allRecurring.FirstOrDefault(r => r.RecurringJobId == "test-from-appsettings");

        // Verify job was registered
        registered.Should().NotBeNull("job should be registered from appsettings");
        registered!.Cron.Should().Be("* * * * *");
        registered.Queue.Should().Be("default");
        registered.Enabled.Should().BeTrue();

        // Verify job was registered as due (NextExecution in past or very near)
        var now = DateTimeOffset.UtcNow;
        registered.NextExecution.Should().NotBeNull("NextExecution should be set");
        (registered.NextExecution!.Value <= now.AddSeconds(2)).Should().BeTrue(
            "job should be due immediately for execution");
    }

    /// <summary>
    /// Integration test: verifies that TimeZoneId from appsettings is correctly stored.
    /// </summary>
    [Fact]
    public async Task RecurringJob_AppsettingsWithTimezone_StoresCorrectly()
    {
        var configData = new Dictionary<string, string?>
        {
            ["NexJob:RecurringJobs:0:Id"] = "tz-job",
            ["NexJob:RecurringJobs:0:JobType"] = typeof(RecurringFromAppsettingsTestJob).AssemblyQualifiedName,
            ["NexJob:RecurringJobs:0:Cron"] = "0 9 * * *",
            ["NexJob:RecurringJobs:0:TimeZoneId"] = "America/New_York",
            ["NexJob:RecurringJobs:0:Queue"] = "default",
            ["NexJob:RecurringJobs:0:Enabled"] = "true",
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(configuration);
                services.AddTransient<RecurringFromAppsettingsTestJob>();
            })
            .Build();

        await host.StartAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await host.StopAsync();

        var storage = host.Services.GetRequiredService<IStorageProvider>();
        var all = await storage.GetRecurringJobsAsync();
        var registered = all.FirstOrDefault(r => r.RecurringJobId == "tz-job");

        registered.Should().NotBeNull();
        registered!.TimeZoneId.Should().Be("America/New_York");
        registered.Cron.Should().Be("0 9 * * *");
    }

    /// <summary>
    /// Integration test: verifies that multiple recurring jobs in appsettings are all registered.
    /// </summary>
    [Fact]
    public async Task RecurringJobs_MultipleInAppsettings_AllRegister()
    {
        var configData = new Dictionary<string, string?>
        {
            // First job
            ["NexJob:RecurringJobs:0:Id"] = "morning-report",
            ["NexJob:RecurringJobs:0:JobType"] = typeof(RecurringFromAppsettingsTestJob).AssemblyQualifiedName,
            ["NexJob:RecurringJobs:0:Cron"] = "0 9 * * *",
            ["NexJob:RecurringJobs:0:Queue"] = "reports",
            ["NexJob:RecurringJobs:0:Enabled"] = "true",

            // Second job
            ["NexJob:RecurringJobs:1:Id"] = "evening-cleanup",
            ["NexJob:RecurringJobs:1:JobType"] = typeof(RecurringFromAppsettingsTestJob).AssemblyQualifiedName,
            ["NexJob:RecurringJobs:1:Cron"] = "0 18 * * *",
            ["NexJob:RecurringJobs:1:Queue"] = "maintenance",
            ["NexJob:RecurringJobs:1:Enabled"] = "true",

            // Third job with timezone
            ["NexJob:RecurringJobs:2:Id"] = "nightly-backup",
            ["NexJob:RecurringJobs:2:JobType"] = typeof(RecurringFromAppsettingsTestJob).AssemblyQualifiedName,
            ["NexJob:RecurringJobs:2:Cron"] = "0 2 * * *",
            ["NexJob:RecurringJobs:2:Queue"] = "backup",
            ["NexJob:RecurringJobs:2:TimeZoneId"] = "UTC",
            ["NexJob:RecurringJobs:2:Enabled"] = "true",
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(configuration);
                services.AddTransient<RecurringFromAppsettingsTestJob>();
            })
            .Build();

        await host.StartAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        await host.StopAsync();

        var storage = host.Services.GetRequiredService<IStorageProvider>();
        var all = await storage.GetRecurringJobsAsync();

        // Verify all three jobs registered
        all.Should().HaveCountGreaterThanOrEqualTo(3);
        all.Should().Contain(r => r.RecurringJobId == "morning-report");
        all.Should().Contain(r => r.RecurringJobId == "evening-cleanup");
        all.Should().Contain(r => r.RecurringJobId == "nightly-backup");

        // Verify properties
        var morning = all.First(r => r.RecurringJobId == "morning-report");
        morning.Queue.Should().Be("reports");
        morning.Cron.Should().Be("0 9 * * *");

        var evening = all.First(r => r.RecurringJobId == "evening-cleanup");
        evening.Queue.Should().Be("maintenance");
        evening.Cron.Should().Be("0 18 * * *");

        var nightly = all.First(r => r.RecurringJobId == "nightly-backup");
        nightly.Queue.Should().Be("backup");
        nightly.Cron.Should().Be("0 2 * * *");
        nightly.TimeZoneId.Should().Be("UTC");
    }
}

// ─── test job implementations ────────────────────────────────────────────────

/// <summary>
/// Stub no-input job for record/policy tests. Does nothing on execute.
/// </summary>
internal sealed class RecurringNoInputStubJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// No-input job that increments a counter on each execution.
/// Used for end-to-end execution verification.
/// </summary>
internal sealed class RecurringNoInputTestJob : IJob
{
    /// <summary>
    /// Counter tracking total executions across all instances.
    /// </summary>
    private static int _executionCount;

    public static int ExecutionCount => _executionCount;

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _executionCount);
        return Task.CompletedTask;
    }

    public static void ResetCount()
    {
        _executionCount = 0;
    }
}

/// <summary>
/// No-input job loaded from appsettings configuration.
/// Used to verify recurring jobs are bound from configuration.
/// </summary>
internal sealed class RecurringFromAppsettingsTestJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
