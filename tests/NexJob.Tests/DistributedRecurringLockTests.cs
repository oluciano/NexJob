using FluentAssertions;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Tests for <see cref="NexJob.Storage.IStorageProvider.TryAcquireRecurringJobLockAsync"/> and
/// <see cref="NexJob.Storage.IStorageProvider.ReleaseRecurringJobLockAsync"/> using
/// <see cref="InMemoryStorageProvider"/>.
/// </summary>
public sealed class DistributedRecurringLockTests
{
    private static InMemoryStorageProvider NewStorage() => new();

    // ─── basic acquire / release ──────────────────────────────────────────────

    [Fact]
    public async Task TryAcquire_FirstCall_ReturnsTrue()
    {
        var storage = NewStorage();

        var acquired = await storage.TryAcquireRecurringJobLockAsync("job-1", TimeSpan.FromSeconds(30));

        acquired.Should().BeTrue("first acquire on a free lock must succeed");
    }

    [Fact]
    public async Task TryAcquire_WhileLockHeld_ReturnsFalse()
    {
        var storage = NewStorage();
        await storage.TryAcquireRecurringJobLockAsync("job-1", TimeSpan.FromSeconds(30));

        var second = await storage.TryAcquireRecurringJobLockAsync("job-1", TimeSpan.FromSeconds(30));

        second.Should().BeFalse("lock is already held — second acquire must fail");
    }

    [Fact]
    public async Task TryAcquire_DifferentJobs_BothSucceed()
    {
        var storage = NewStorage();

        var a = await storage.TryAcquireRecurringJobLockAsync("job-a", TimeSpan.FromSeconds(30));
        var b = await storage.TryAcquireRecurringJobLockAsync("job-b", TimeSpan.FromSeconds(30));

        a.Should().BeTrue();
        b.Should().BeTrue("different job IDs use independent locks");
    }

    [Fact]
    public async Task Release_AllowsReacquire()
    {
        var storage = NewStorage();
        await storage.TryAcquireRecurringJobLockAsync("job-1", TimeSpan.FromSeconds(30));
        await storage.ReleaseRecurringJobLockAsync("job-1");

        var reacquired = await storage.TryAcquireRecurringJobLockAsync("job-1", TimeSpan.FromSeconds(30));

        reacquired.Should().BeTrue("after release the lock must be acquirable again");
    }

    [Fact]
    public async Task Release_NonexistentLock_DoesNotThrow()
    {
        var storage = NewStorage();

        var act = async () => await storage.ReleaseRecurringJobLockAsync("never-acquired");

        await act.Should().NotThrowAsync("releasing a non-existent lock must be a no-op");
    }

    // ─── TTL expiry ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Lock_ExpiresAfterTtl()
    {
        var storage = NewStorage();
        await storage.TryAcquireRecurringJobLockAsync("job-1", TimeSpan.FromMilliseconds(50));

        // Wait for TTL to elapse
        await Task.Delay(150);

        var reacquired = await storage.TryAcquireRecurringJobLockAsync("job-1", TimeSpan.FromSeconds(30));

        reacquired.Should().BeTrue("expired lock must be treated as released");
    }

    // ─── concurrent contention ────────────────────────────────────────────────

    [Fact]
    public async Task TwoSchedulers_OnlyOneAcquiresLock()
    {
        var storage = NewStorage();
        const int concurrency = 10;

        var results = await Task.WhenAll(
            Enumerable.Range(0, concurrency)
                .Select(_ => storage.TryAcquireRecurringJobLockAsync("shared-job", TimeSpan.FromSeconds(30))));

        results.Count(r => r).Should().Be(1, "exactly one concurrent caller must acquire the lock");
        results.Count(r => !r).Should().Be(concurrency - 1, "all other callers must be rejected");
    }
}
