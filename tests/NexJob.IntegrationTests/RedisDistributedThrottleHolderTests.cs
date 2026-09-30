using FluentAssertions;
using NexJob.Redis;
using StackExchange.Redis;
using Xunit;

namespace NexJob.IntegrationTests;

/// <summary>
/// Issue #267 (part 2): distributed throttle slots are per-holder entries with an expiry that the owning node keeps
/// refreshing, so a crashed node's slots are reclaimed within seconds instead of an hour.
/// </summary>
[Collection("Redis")]
public sealed class RedisDistributedThrottleHolderTests
{
    private static readonly TimeSpan Heartbeat = TimeSpan.FromMilliseconds(100);

    private readonly RedisFixture _fixture;

    public RedisDistributedThrottleHolderTests(RedisFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>N1 (Positive): a live holder is refreshed and keeps its slot well past the holder TTL.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task LiveHolder_IsRefreshed_AndKeepsItsSlot()
    {
        var db = await ConnectAsync();
        var resource = NewResource();
        using var nodeA = NewStore(db);
        using var nodeB = NewStore(db);
        (await nodeA.TryAcquireAsync(resource, 1)).Should().BeTrue();

        await Task.Delay(TimeSpan.FromMilliseconds(900)); // three holder TTLs

        (await nodeB.TryAcquireAsync(resource, 1)).Should().BeFalse("the holder is alive and refreshing its entry");
    }

    /// <summary>N2 (Negative): a holder whose node stopped refreshing (crash) is reclaimed after the holder TTL.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task HolderOfACrashedNode_IsReclaimedAfterTheTtl()
    {
        var db = await ConnectAsync();
        var resource = NewResource();
        var crashed = NewStore(db);
        using var survivor = NewStore(db);
        (await crashed.TryAcquireAsync(resource, 1)).Should().BeTrue();
        crashed.Dispose(); // the node dies: nothing releases or refreshes the slot

        (await survivor.TryAcquireAsync(resource, 1)).Should().BeFalse("the entry has not expired yet");
        await Task.Delay(TimeSpan.FromMilliseconds(700));

        (await survivor.TryAcquireAsync(resource, 1)).Should().BeTrue("the dead node's slot expired and was reclaimed");
    }

    /// <summary>N3 (Boundary): releasing without holding, and releasing one holder, never touches another node's slots.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Release_OnlyRemovesTheCallersOwnHolder()
    {
        var db = await ConnectAsync();
        var resource = NewResource();
        using var nodeA = NewStore(db);
        using var nodeB = NewStore(db);
        (await nodeA.TryAcquireAsync(resource, 2)).Should().BeTrue();
        (await nodeB.TryAcquireAsync(resource, 2)).Should().BeTrue();
        var key = $"nexjob:throttle:holders:{resource}";

        await nodeA.ReleaseAsync(resource);
        await nodeA.ReleaseAsync(resource); // nothing left to release on this node

        (await db.SortedSetLengthAsync(key)).Should().Be(1, "node B's holder must survive");
    }

    /// <summary>N3 (Boundary): the maximum hold time caps a slot even if the node keeps refreshing.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task SlotHeldPastTheMaximumHoldTime_IsNotRefreshedAnyMore()
    {
        var db = await ConnectAsync();
        var resource = NewResource();
        var options = new NexJobOptions { HeartbeatInterval = Heartbeat, DistributedThrottleTtl = TimeSpan.FromSeconds(1) };
        using var holder = new RedisDistributedThrottleStore(db, options);
        using var other = NewStore(db);
        (await holder.TryAcquireAsync(resource, 1)).Should().BeTrue();

        await Task.Delay(TimeSpan.FromMilliseconds(2000));

        (await other.TryAcquireAsync(resource, 1)).Should().BeTrue("a slot older than DistributedThrottleTtl stops being refreshed and expires");
    }

    /// <summary>N1 (Positive): concurrent acquires never exceed the limit.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ConcurrentAcquires_NeverExceedTheLimit()
    {
        var db = await ConnectAsync();
        var resource = NewResource();
        using var store = NewStore(db);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => store.TryAcquireAsync(resource, 3)));

        results.Count(r => r).Should().Be(3);
    }

    private static string NewResource() => $"res_{Guid.NewGuid():N}";

    private static RedisDistributedThrottleStore NewStore(IDatabase db) =>
        new(db, new NexJobOptions { HeartbeatInterval = Heartbeat });

    private async Task<IDatabase> ConnectAsync() =>
        (await ConnectionMultiplexer.ConnectAsync(_fixture.Container.GetConnectionString())).GetDatabase();
}
