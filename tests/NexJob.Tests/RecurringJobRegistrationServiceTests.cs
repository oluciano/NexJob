using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob.Configuration;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Tests for <see cref="RecurringJobRegistrationService"/>, the start-up registration of the recurring jobs listed in the configuration.
/// </summary>
public sealed class RecurringJobRegistrationServiceTests
{
    /// <summary>
    /// N2 (Negative, #642): when the storage is down at start-up the registration is logged and given up: the service ends
    /// after a single attempt, so the configured recurring jobs are not registered until the host restarts.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task StorageDownAtStart_RegistrationIsAttemptedOnceAndGivenUp()
    {
        var storage = new Mock<IRecurringStorage>();
        storage.Setup(x => x.UpsertRecurringJobAsync(It.IsAny<RecurringJobRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("storage down"));
        var sut = BuildService(storage.Object);

        await sut.StartAsync(CancellationToken.None);
        await sut.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        sut.ExecuteTask.IsCompletedSuccessfully.Should().BeTrue("the failure is swallowed and the service ends");
        storage.Verify(x => x.UpsertRecurringJobAsync(It.IsAny<RecurringJobRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        await sut.StopAsync(CancellationToken.None);
    }

    /// <summary>N1 (Positive, #642): with the storage up the configured recurring job is registered.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task StorageUp_RegistersTheConfiguredJob()
    {
        var storage = new InMemoryStorageProvider();
        var sut = BuildService(storage);

        await sut.StartAsync(CancellationToken.None);
        await sut.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        (await storage.GetRecurringJobsAsync()).Should().ContainSingle();
        await sut.StopAsync(CancellationToken.None);
    }

    /// <summary>N3 (Invalid input, #642): an empty configuration registers nothing and the service ends cleanly.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task EmptyConfiguration_RegistersNothing()
    {
        var storage = new InMemoryStorageProvider();
        var sut = BuildService(storage, jobs: []);

        await sut.StartAsync(CancellationToken.None);
        await sut.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        (await storage.GetRecurringJobsAsync()).Should().BeEmpty();
        await sut.StopAsync(CancellationToken.None);
    }

    private static RecurringJobRegistrationService BuildService(IRecurringStorage storage, IReadOnlyList<RecurringJobSettings>? jobs = null)
    {
        var registry = new NexJobJobRegistry();
        registry.Register(typeof(TestJob1));
        var options = new NexJobOptions
        {
            RecurringJobs = [.. jobs ?? [new RecurringJobSettings { Job = "TestJob1", Cron = "0 0 * * *" }]],
        };
        var registrar = new RecurringJobRegistrar(storage, registry, NullLogger<RecurringJobRegistrar>.Instance);
        return new RecurringJobRegistrationService(options, registrar, NullLogger<RecurringJobRegistrationService>.Instance);
    }
}
