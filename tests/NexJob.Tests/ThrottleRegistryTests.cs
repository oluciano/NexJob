using FluentAssertions;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

public sealed class ThrottleRegistryTests
{
    private readonly ThrottleRegistry _sut = new();

    [Fact]
    public async Task TryAcquireAsync_ReturnsTrue_WithAvailableSlots()
    {
        var result = await _sut.TryAcquireAsync("resource-a", maxConcurrent: 3, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_ReturnsFalse_WhenAllSlotsAcquired()
    {
        const int max = 2;
        await _sut.TryAcquireAsync("resource-b", maxConcurrent: max, CancellationToken.None);
        await _sut.TryAcquireAsync("resource-b", maxConcurrent: max, CancellationToken.None);

        var result = await _sut.TryAcquireAsync("resource-b", maxConcurrent: max, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ReleaseAsync_FreesSlot()
    {
        const int max = 1;
        await _sut.TryAcquireAsync("resource-c", maxConcurrent: max, CancellationToken.None);
        (await _sut.TryAcquireAsync("resource-c", maxConcurrent: max, CancellationToken.None)).Should().BeFalse();

        await _sut.ReleaseAsync("resource-c", CancellationToken.None);

        (await _sut.TryAcquireAsync("resource-c", maxConcurrent: max, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_UsesDifferentSemaphores_ForDifferentResources()
    {
        await _sut.TryAcquireAsync("resource-d", maxConcurrent: 1, CancellationToken.None);

        var result = await _sut.TryAcquireAsync("resource-e", maxConcurrent: 1, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_SecondCallWithDifferentMax_UsesOriginalMax()
    {
        await _sut.TryAcquireAsync("resource-f", maxConcurrent: 1, CancellationToken.None);

        // This should still return false because it uses the first max (1)
        var result = await _sut.TryAcquireAsync("resource-f", maxConcurrent: 5, CancellationToken.None);

        result.Should().BeFalse();
    }
}
