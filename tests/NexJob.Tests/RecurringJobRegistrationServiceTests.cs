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
    /// N2 (Negative, #648): with the storage down for the whole run the service keeps retrying with backoff, never throws,
    /// and stops retrying when the host stops.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    // Behavior changed in v6.7.0: a storage failure at start used to be attempted once and given up (#642); it is retried with backoff (#648).
    public async Task StorageDownAtStart_KeepsRetryingUntilTheHostStops()
    {
        var upserts = 0;
        var storage = new Mock<IRecurringStorage>();
        storage.Setup(x => x.UpsertRecurringJobAsync(It.IsAny<RecurringJobRecord>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref upserts);
                return Task.FromException(new InvalidOperationException("storage down"));
            });
        var sut = BuildService(storage.Object);

        await sut.StartAsync(CancellationToken.None);

        await TestWait.UntilAsync(() => Task.FromResult(Volatile.Read(ref upserts) >= 3), TimeSpan.FromSeconds(20), "the registration keeps being retried");
        sut.ExecuteTask!.IsCompleted.Should().BeFalse("the service waits for the storage instead of giving up");
        await sut.StopAsync(CancellationToken.None);
        sut.ExecuteTask.IsCompleted.Should().BeTrue("stopping the host ends the retry loop");
    }

    /// <summary>N1 (Positive, #648): the storage fails twice and then answers; the job is registered and the service ends.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task StorageFailsTwiceThenAnswers_TheJobIsRegisteredAndTheServiceEnds()
    {
        var calls = 0;
        var stored = 0;
        var storage = new Mock<IRecurringStorage>();
        storage.Setup(x => x.UpsertRecurringJobAsync(It.IsAny<RecurringJobRecord>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (Interlocked.Increment(ref calls) <= 2)
                {
                    return Task.FromException(new InvalidOperationException("storage down"));
                }

                Interlocked.Increment(ref stored);
                return Task.CompletedTask;
            });
        var sut = BuildService(storage.Object);

        await sut.StartAsync(CancellationToken.None);
        await sut.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(20));

        Volatile.Read(ref stored).Should().Be(1, "the job is stored once, on the first attempt that works");
        Volatile.Read(ref calls).Should().Be(3);
        await sut.StopAsync(CancellationToken.None);
    }

    /// <summary>N3 (Invalid input, #648): a configuration error is permanent, so it is not retried and the service ends.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task ConfigurationError_IsNotRetried()
    {
        var storage = new Mock<IRecurringStorage>();
        var sut = BuildService(storage.Object, jobs: [new RecurringJobSettings { Job = "TestJob1", Cron = "not a cron" }]);

        await sut.StartAsync(CancellationToken.None);
        await sut.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        storage.Verify(x => x.UpsertRecurringJobAsync(It.IsAny<RecurringJobRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        await sut.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// N3 (Boundary, #648): one job has a configuration error and another hits a storage failure. Only the storage failure is
    /// retried, and the bad job does not stop the good one from being stored.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task BadJobAndStorageFailure_OnlyTheStorageFailureIsRetried()
    {
        var calls = 0;
        var storage = new Mock<IRecurringStorage>();
        storage.Setup(x => x.UpsertRecurringJobAsync(It.IsAny<RecurringJobRecord>(), It.IsAny<CancellationToken>()))
            .Returns(() => Interlocked.Increment(ref calls) == 1
                ? Task.FromException(new InvalidOperationException("storage down"))
                : Task.CompletedTask);
        var sut = BuildService(
            storage.Object,
            jobs:
            [
                new RecurringJobSettings { Job = "TestJob1", Cron = "not a cron", Id = "bad" },
                new RecurringJobSettings { Job = "TestJob1", Cron = "0 0 * * *", Id = "good" },
            ]);

        await sut.StartAsync(CancellationToken.None);
        await sut.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(20));

        storage.Verify(x => x.UpsertRecurringJobAsync(It.Is<RecurringJobRecord>(r => r.RecurringJobId == "good"), It.IsAny<CancellationToken>()), Times.Exactly(2));
        storage.Verify(x => x.UpsertRecurringJobAsync(It.Is<RecurringJobRecord>(r => r.RecurringJobId == "bad"), It.IsAny<CancellationToken>()), Times.Never);
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
