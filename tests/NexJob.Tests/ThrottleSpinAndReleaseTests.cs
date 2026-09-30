using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Tests for issue #267 (part 1): a saturated distributed throttle must not be polled in a hot loop, and a slot
/// that was never taken from the distributed store must never be released to it.
/// </summary>
public sealed class ThrottleSpinAndReleaseTests
{
    private sealed class CountingStore : IDistributedThrottleStore
    {
        private int _acquireCalls;
        private int _releaseCalls;

        public bool AcquireResult { get; init; }

        public bool AcquireThrows { get; init; }

        public int AcquireCalls => Volatile.Read(ref _acquireCalls);

        public int ReleaseCalls => Volatile.Read(ref _releaseCalls);

        public Task<bool> TryAcquireAsync(string resource, int maxConcurrent, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _acquireCalls);
            return AcquireThrows ? throw new InvalidOperationException("redis down") : Task.FromResult(AcquireResult);
        }

        public Task ReleaseAsync(string resource, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _releaseCalls);
            return Task.CompletedTask;
        }
    }

    // ─── N1: bounded polling of a full throttle ──────────────────────────────

    /// <summary>N1 (Positive): with the throttle full, the executor asks the distributed store only a few times per second.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task FullDistributedThrottle_AcquireCallsPerSecond_AreBounded()
    {
        var store = new CountingStore { AcquireResult = false };
        await using var sut = BuildExecutor(store, out var job);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await sut.ExecuteJobAsync(job, cts.Token);

        store.AcquireCalls.Should().BeInRange(1, 8, "a waiting job must back off instead of spinning against Redis");
    }

    // ─── N2: no release of a slot that was never acquired ────────────────────

    /// <summary>N2 (Negative): when the distributed store fails and the registry degrades to local, release does not call the store.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task DistributedAcquireThrows_LocalOnly_ReleaseDoesNotCallStore()
    {
        var store = new CountingStore { AcquireThrows = true };
        var registry = new ThrottleRegistry(store, NullLogger<ThrottleRegistry>.Instance);

        (await registry.TryAcquireWithWaitAsync("r", 1, TimeSpan.FromMilliseconds(50), CancellationToken.None)).Should().BeTrue();
        await registry.ReleaseAsync("r", CancellationToken.None);

        store.ReleaseCalls.Should().Be(0);
    }

    /// <summary>N1 (Positive): a slot acquired from the distributed store is released to it exactly once.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task DistributedAcquired_ReleaseCallsStoreOnce()
    {
        var store = new CountingStore { AcquireResult = true };
        var registry = new ThrottleRegistry(store, NullLogger<ThrottleRegistry>.Instance);

        (await registry.TryAcquireWithWaitAsync("r", 2, TimeSpan.FromMilliseconds(50), CancellationToken.None)).Should().BeTrue();
        await registry.ReleaseAsync("r", CancellationToken.None);

        store.ReleaseCalls.Should().Be(1);
    }

    /// <summary>N3 (Invalid input): releasing more times than acquired never over-releases the distributed counter.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ReleaseWithoutAcquire_DoesNotCallStore()
    {
        var store = new CountingStore { AcquireResult = true };
        var registry = new ThrottleRegistry(store, NullLogger<ThrottleRegistry>.Instance);

        await registry.ReleaseAsync("never-acquired", CancellationToken.None);

        store.ReleaseCalls.Should().Be(0);
    }

    // ─── N3: cancellation while waiting ──────────────────────────────────────

    /// <summary>N3 (Boundary): cancelling while waiting on a full throttle returns promptly and leaks no slot.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task CancelWhileWaiting_ExitsPromptly_NoSlotLeaked()
    {
        var store = new CountingStore { AcquireResult = false };
        var registry = new ThrottleRegistry(store, NullLogger<ThrottleRegistry>.Instance);
        await using var sut = BuildExecutor(registry, out var job);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var sw = Stopwatch.StartNew();

        await sut.ExecuteJobAsync(job, cts.Token);

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        store.ReleaseCalls.Should().Be(0, "nothing was acquired, so nothing may be released");
        store.AcquireCalls.Should().BeGreaterThan(0);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static JobExecutor BuildExecutor(IDistributedThrottleStore store, out JobRecord job) =>
        BuildExecutor(new ThrottleRegistry(store, NullLogger<ThrottleRegistry>.Instance), out job);

    private static JobExecutor BuildExecutor(ThrottleRegistry registry, out JobRecord job)
    {
        job = new JobRecord { Id = JobId.New(), JobType = "TestJob", Attempts = 1, MaxAttempts = 3 };
        var invoker = new Mock<IJobInvokerFactory>();
        var context = new JobInvocationContext(
            new Mock<IServiceScope>().Object,
            new object(),
            new object(),
            (object _, object _, CancellationToken _) => Task.CompletedTask,
            [new ThrottleAttribute("api", 1)]);
        var captured = job;
        invoker.Setup(x => x.PrepareAsync(captured, It.IsAny<CancellationToken>())).ReturnsAsync(context);

        return new JobExecutor(
            new Mock<IJobStorage>().Object,
            invoker.Object,
            new Mock<IJobRetryPolicy>().Object,
            new Mock<IDeadLetterDispatcher>().Object,
            registry,
            new NexJobOptions { HeartbeatInterval = TimeSpan.FromMinutes(5) },
            Enumerable.Empty<IJobExecutionFilter>(),
            NullLogger<JobExecutor>.Instance);
    }
}
