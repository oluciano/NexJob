using FluentAssertions;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

public sealed class ThrottleRegistryTests
{
    private readonly ThrottleRegistry _sut = new();

    // ─── GetOrCreate ──────────────────────────────────────────────────────────

    [Fact]
    public void GetOrCreate_ReturnsSemaphore_WithCorrectMaxConcurrent()
    {
        var sem = _sut.GetOrCreate("resource-a", maxConcurrent: 3);

        sem.Should().NotBeNull();
        sem.CurrentCount.Should().Be(3);
    }

    [Fact]
    public void GetOrCreate_ReturnsSameSemaphore_ForSameResource()
    {
        var first = _sut.GetOrCreate("resource-b", maxConcurrent: 2);
        var second = _sut.GetOrCreate("resource-b", maxConcurrent: 2);

        second.Should().BeSameAs(first, "repeated calls for the same resource must return the cached instance");
    }

    [Fact]
    public void GetOrCreate_ReturnsDifferentSemaphores_ForDifferentResources()
    {
        var semA = _sut.GetOrCreate("resource-c", maxConcurrent: 1);
        var semB = _sut.GetOrCreate("resource-d", maxConcurrent: 1);

        semB.Should().NotBeSameAs(semA, "distinct resource names must produce independent semaphores");
    }

    [Fact]
    public async Task GetOrCreate_EnforcesMaxConcurrent_BlocksWhenAllSlotsAcquired()
    {
        const int max = 2;
        var sem = _sut.GetOrCreate("resource-e", maxConcurrent: max);

        // Acquire all available slots
        for (var i = 0; i < max; i++)
        {
            await sem.WaitAsync();
        }

        // The semaphore should now be exhausted — a subsequent WaitAsync must not complete immediately
        var acquired = await sem.WaitAsync(millisecondsTimeout: 0);

        acquired.Should().BeFalse("all slots are held so the next acquire must be blocked");

        // Release so the semaphore is left in a clean state
        sem.Release(max);
    }

    [Fact]
    public void GetOrCreate_SecondCallWithDifferentMax_ReturnsOriginalSemaphore()
    {
        // The registry ignores the maxConcurrent parameter on subsequent calls for the same key.
        var first = _sut.GetOrCreate("resource-f", maxConcurrent: 5);
        var second = _sut.GetOrCreate("resource-f", maxConcurrent: 99);

        second.Should().BeSameAs(first, "the original semaphore is returned regardless of the new maxConcurrent value");
        second.CurrentCount.Should().Be(5, "the original capacity must not be changed by a subsequent call");
    }
}
