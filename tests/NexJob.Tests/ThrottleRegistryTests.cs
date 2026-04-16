using FluentAssertions;
using Moq;
using NexJob.Redis;
using Xunit;

namespace NexJob.Internal.Tests;

public sealed class ThrottleRegistryTests
{
    [Fact]
    public async Task TryAcquireWithWaitAsync_SlotAvailable_ReturnsTrue()
    {
        // Arrange
        var registry = new ThrottleRegistry();

        // Act
        var result = await registry.TryAcquireWithWaitAsync("res1", 1, TimeSpan.FromMilliseconds(100), CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireWithWaitAsync_SlotOccupied_WaitsAndAcquiresAfterRelease()
    {
        // Arrange
        var registry = new ThrottleRegistry();
        await registry.TryAcquireAsync("res1", 1, CancellationToken.None); // Occupy slot

        // Task to release after 200ms
        _ = Task.Run(async () =>
        {
            await Task.Delay(200);
            await registry.ReleaseAsync("res1", CancellationToken.None);
        });

        // Act
        var result = await registry.TryAcquireWithWaitAsync("res1", 1, TimeSpan.FromMilliseconds(500), CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireWithWaitAsync_SlotOccupied_TimesOut_ReturnsFalse()
    {
        // Arrange
        var registry = new ThrottleRegistry();
        await registry.TryAcquireAsync("res1", 1, CancellationToken.None); // Occupy slot permanent for this test

        // Act
        var result = await registry.TryAcquireWithWaitAsync("res1", 1, TimeSpan.FromMilliseconds(100), CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireWithWaitAsync_Cancelled_ThrowsOperationCancelled()
    {
        // Arrange
        var registry = new ThrottleRegistry();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            registry.TryAcquireWithWaitAsync("res1", 1, TimeSpan.FromMilliseconds(100), cts.Token));
    }

    // ─── TryAcquireAsync distributed branches ────────────────────────────────

    [Fact]
    public async Task TryAcquireAsync_DistributedAcquired_LocalFull_ReleasesDistributed_ReturnsFalse()
    {
        // Arrange — distributed always grants, local semaphore maxConcurrent=1
        var storeMock = new Mock<IDistributedThrottleStore>();
        storeMock.Setup(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var registry = new ThrottleRegistry(storeMock.Object);

        // Pre-fill the local slot so the second call finds it full
        await registry.TryAcquireAsync("res1", 1, CancellationToken.None);

        // Act — distributed grants again, but local is full → must rollback distributed
        var result = await registry.TryAcquireAsync("res1", 1, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        storeMock.Verify(s => s.ReleaseAsync("res1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryAcquireAsync_DistributedThrows_DegradesToLocal_ReturnsTrue()
    {
        // Arrange — distributed store unavailable
        var storeMock = new Mock<IDistributedThrottleStore>();
        storeMock.Setup(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("distributed store unavailable"));

        var registry = new ThrottleRegistry(storeMock.Object);

        // Act — should degrade to local semaphore and succeed
        var result = await registry.TryAcquireAsync("res1", 1, CancellationToken.None);

        // Assert
        result.Should().BeTrue("local slot must be acquired when distributed store is unavailable");
    }

    [Fact]
    public async Task TryAcquireAsync_CancelledWhileWaitingLocal_DistributedAcquired_ReleasesDistributed()
    {
        // Arrange — distributed grants, local slot is full, token pre-cancelled
        var storeMock = new Mock<IDistributedThrottleStore>();
        storeMock.Setup(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var registry = new ThrottleRegistry(storeMock.Object);

        // Fill the local slot (maxConcurrent=1)
        await registry.TryAcquireAsync("res1", 1, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act — distributed grants, but local WaitAsync(0, cancelledToken) throws
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            registry.TryAcquireAsync("res1", 1, cts.Token));

        // Assert — distributed slot must be rolled back
        storeMock.Verify(s => s.ReleaseAsync("res1", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── TryAcquireWithWaitAsync distributed branches ─────────────────────────

    [Fact]
    public async Task TryAcquireWithWaitAsync_DistributedAcquired_LocalFull_ReleasesDistributed_ReturnsFalse()
    {
        // Arrange — distributed always grants, local slot is full
        var storeMock = new Mock<IDistributedThrottleStore>();
        storeMock.Setup(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var registry = new ThrottleRegistry(storeMock.Object);

        // Fill the local slot
        await registry.TryAcquireAsync("res1", 1, CancellationToken.None);

        // Act — distributed grants, local times out → must rollback distributed
        var result = await registry.TryAcquireWithWaitAsync("res1", 1, TimeSpan.FromMilliseconds(50), CancellationToken.None);

        // Assert
        result.Should().BeFalse();
        storeMock.Verify(s => s.ReleaseAsync("res1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TryAcquireWithWaitAsync_DistributedThrows_DegradesToLocal_ReturnsTrue()
    {
        // Arrange — distributed store unavailable
        var storeMock = new Mock<IDistributedThrottleStore>();
        storeMock.Setup(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("distributed store unavailable"));

        var registry = new ThrottleRegistry(storeMock.Object);

        // Act — should degrade to local semaphore with wait
        var result = await registry.TryAcquireWithWaitAsync("res1", 1, TimeSpan.FromMilliseconds(100), CancellationToken.None);

        // Assert
        result.Should().BeTrue("local slot must be acquired when distributed store is unavailable");
    }
}
