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
}
