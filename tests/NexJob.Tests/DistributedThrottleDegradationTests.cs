using FluentAssertions;
using Moq;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Tests that the throttle system degrades gracefully when the distributed
/// store is unavailable — jobs must still execute, bounded by local semaphore.
/// </summary>
public sealed class DistributedThrottleDegradationTests
{
    /// <summary>
    /// Verifies that if the distributed store throws an exception, the system
    /// degrades to the local throttle instead of propagating the error.
    /// </summary>
    [Fact]
    public async Task WhenDistributedStoreFails_ThrottleRegistry_FallsBackToLocal()
    {
        // Setup: create a ThrottleRegistry with a IDistributedThrottleStore mock
        //        that always throws on TryAcquireAsync
        var store = new Mock<IDistributedThrottleStore>();
        store.Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Redis down"));

        var registry = new ThrottleRegistry(store.Object);

        // Act: call TryAcquireWithWaitAsync
        // Even if distributed throws, it should proceed to local semaphore.
        // With maxConcurrent=1, the first call should succeed locally.
        var result = await registry.TryAcquireWithWaitAsync("res", 1, TimeSpan.FromMilliseconds(100), CancellationToken.None);

        // Assert: does NOT throw — returns true because it acquired the local slot
        result.Should().BeTrue("should have acquired local slot even if distributed store failed");

        // Cleanup
        await registry.ReleaseAsync("res", CancellationToken.None);
    }

    /// <summary>
    /// Verifies that even when the distributed store is down, the local semaphore
    /// still enforces the concurrency limits.
    /// </summary>
    [Fact]
    public async Task WhenDistributedStoreUnavailable_LocalThrottleStillEnforcesLimit()
    {
        // Setup: ThrottleRegistry with failing distributed store
        //        maxConcurrent = 2
        var store = new Mock<IDistributedThrottleStore>();
        store.Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Redis down"));

        var registry = new ThrottleRegistry(store.Object);

        // Act: TryAcquireWithWaitAsync 3 times simultaneously
        var t1 = registry.TryAcquireWithWaitAsync("res", 2, TimeSpan.FromSeconds(5), CancellationToken.None);
        var t2 = registry.TryAcquireWithWaitAsync("res", 2, TimeSpan.FromSeconds(5), CancellationToken.None);
        var t3 = registry.TryAcquireWithWaitAsync("res", 2, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        var results = await Task.WhenAll(t1, t2, t3);

        // Assert: exactly 2 succeed, 1 times out
        // (local semaphore still bounds concurrency even when distributed is down)
        results.Count(r => r).Should().Be(2, "exactly 2 slots should be acquired locally");
        results.Count(r => !r).Should().Be(1, "third request should time out locally");
    }
}
