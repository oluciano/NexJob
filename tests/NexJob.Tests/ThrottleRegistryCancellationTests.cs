using FluentAssertions;
using Moq;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// When a throttle wait is cancelled or times out, the distributed slot taken for it must be given back, and giving it
/// back must not be cancelled by the very token that just fired (#324).
/// </summary>
public sealed class ThrottleRegistryCancellationTests
{
    private static Mock<IDistributedThrottleStore> StoreThatAlwaysGrants()
    {
        var store = new Mock<IDistributedThrottleStore>();
        store.Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return store;
    }

    // ─── N1: cancelled while waiting for the local slot ───────────────────────

    [Fact]
    public async Task CancelledWait_ReleasesTheDistributedSlot_WithATokenThatIsNotCancelled()
    {
        var store = StoreThatAlwaysGrants();
        var registry = new ThrottleRegistry(store.Object);
        (await registry.TryAcquireWithWaitAsync("res", 1, TimeSpan.FromMilliseconds(50), CancellationToken.None))
            .Should().BeTrue("the first call takes the only local slot");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Func<Task> act = () => registry.TryAcquireWithWaitAsync("res", 1, TimeSpan.FromSeconds(10), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        store.Verify(
            x => x.ReleaseAsync("res", It.Is<CancellationToken>(t => !t.IsCancellationRequested)),
            Times.Once,
            "the release has to complete even though the wait was cancelled");
    }

    // ─── N2: the local wait times out ─────────────────────────────────────────

    [Fact]
    public async Task TimedOutWait_ReleasesTheDistributedSlotItTook_AndReturnsFalse()
    {
        var store = StoreThatAlwaysGrants();
        var registry = new ThrottleRegistry(store.Object);
        await registry.TryAcquireWithWaitAsync("res", 1, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        var acquired = await registry.TryAcquireWithWaitAsync("res", 1, TimeSpan.FromMilliseconds(100), CancellationToken.None);

        acquired.Should().BeFalse();
        store.Verify(x => x.ReleaseAsync("res", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── N3: a release that itself fails never escapes ────────────────────────

    [Fact]
    public async Task FailingRelease_IsSwallowed_WhenTheWaitIsCancelled()
    {
        var store = StoreThatAlwaysGrants();
        store.Setup(x => x.ReleaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis down"));
        var registry = new ThrottleRegistry(store.Object);
        await registry.TryAcquireWithWaitAsync("res", 1, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Func<Task> act = () => registry.TryAcquireWithWaitAsync("res", 1, TimeSpan.FromSeconds(10), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>("only the cancellation surfaces, not the release failure");
    }
}
