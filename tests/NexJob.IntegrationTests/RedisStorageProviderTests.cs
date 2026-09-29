using FluentAssertions;
using NexJob.Redis;
using NexJob.Storage;
using Testcontainers.Redis;
using Xunit;

namespace NexJob.IntegrationTests;

/// <summary>
/// Runs the full <see cref="StorageProviderTestsBase"/> contract against a real
/// Redis instance spun up via Testcontainers.
/// Requires Docker to be available on the host.
/// </summary>
public sealed class RedisStorageProviderTests : StorageProviderTestsBase, IClassFixture<RedisFixture>
{
    private readonly RedisFixture _fixture;

    public RedisStorageProviderTests(RedisFixture fixture)
    {
        _fixture = fixture;

        var connection = StackExchange.Redis.ConnectionMultiplexer.Connect(_fixture.Container.GetConnectionString());
        connection.GetDatabase().Execute("FLUSHDB");
    }

    protected override Task<(IJobStorage Job, IRecurringStorage Recurring, IDashboardStorage Dashboard, IStorageProvider Full)> CreateStorageAsync()
    {
        var provider = new RedisStorageProvider(
            StackExchange.Redis.ConnectionMultiplexer
                .Connect(_fixture.Container.GetConnectionString())
                .GetDatabase());

        return Task.FromResult<(IJobStorage, IRecurringStorage, IDashboardStorage, IStorageProvider)>((provider, provider, provider, provider));
    }

    // ── Purge removes every key of a job (issue #233) ─────────────────────────

    [Fact]
    public async Task PurgeJobsAsync_RemovesLogsKeyTogetherWithJobKey()
    {
        var (mux, provider) = await ConnectAsync();
        var id = await CreateSucceededJobAsync(provider);
        await Task.Delay(200);

        var deleted = await provider.PurgeJobsAsync(new RetentionPolicy { RetainSucceeded = TimeSpan.FromMilliseconds(50) });

        deleted.Should().Be(1);
        (await mux.GetDatabase().KeyExistsAsync($"nexjob:jobs:{id}")).Should().BeFalse();
        (await mux.GetDatabase().KeyExistsAsync($"nexjob:logs:{id}")).Should().BeFalse("purge must not leave the logs key behind");
    }

    [Fact]
    public async Task PurgeJobsAsync_KeepsJobAndLogsKeysWithinRetention()
    {
        var (mux, provider) = await ConnectAsync();
        var id = await CreateSucceededJobAsync(provider);

        var deleted = await provider.PurgeJobsAsync(new RetentionPolicy { RetainSucceeded = TimeSpan.FromDays(7) });

        deleted.Should().Be(0);
        (await mux.GetDatabase().KeyExistsAsync($"nexjob:jobs:{id}")).Should().BeTrue();
        (await mux.GetDatabase().KeyExistsAsync($"nexjob:logs:{id}")).Should().BeTrue();
    }

    [Fact]
    public async Task PurgeJobsAsync_OnEmptyStoreReturnsZero()
    {
        var (_, provider) = await ConnectAsync();

        var deleted = await provider.PurgeJobsAsync(new RetentionPolicy { RetainSucceeded = TimeSpan.FromMilliseconds(50) });

        deleted.Should().Be(0);
    }

    [Fact]
    public async Task PurgeJobsAsync_AcrossBatchesCountsEachJobOnceAndLeavesNoLogsKeys()
    {
        var (mux, provider) = await ConnectAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add(await CreateSucceededJobAsync(provider));
        }

        await Task.Delay(200);

        var deleted = await provider.PurgeJobsAsync(new RetentionPolicy
        {
            RetainSucceeded = TimeSpan.FromMilliseconds(50),
            BatchSize = 2,
        });

        deleted.Should().Be(5, "each purged job is counted exactly once");
        foreach (var id in ids)
        {
            (await mux.GetDatabase().KeyExistsAsync($"nexjob:jobs:{id}")).Should().BeFalse();
            (await mux.GetDatabase().KeyExistsAsync($"nexjob:logs:{id}")).Should().BeFalse();
        }
    }

    // ── Orphan requeue is guarded by the current job state (issue #240) ───────

    [Fact]
    public async Task RequeueOrphanedJobsAsync_DoesNotTouchAJobThatIsNoLongerProcessing()
    {
        var (mux, provider) = await ConnectAsync();
        var db = mux.GetDatabase();
        var id = await CreateProcessingJobAsync(provider);
        await Task.Delay(20);

        // What a concurrent commit leaves behind while the scan already holds the stale entry:
        // the job hash is terminal, the processing entry is still there.
        await db.HashSetAsync($"nexjob:jobs:{id}", "status", "Succeeded");

        await provider.RequeueOrphanedJobsAsync(TimeSpan.Zero);

        ((string?)await db.HashGetAsync($"nexjob:jobs:{id}", "status")).Should().Be("Succeeded", "a finished job must never be re-enqueued");
        (await db.HashExistsAsync("nexjob:processing", id.ToString())).Should().BeFalse("the stale processing entry is cleaned up");
        (await db.SortedSetScoreAsync("nexjob:queue:default:z", id.ToString())).Should().BeNull("the finished job must not be put back in its queue");
    }

    [Fact]
    public async Task RequeueOrphanedJobsAsync_RemovesTheProcessingEntryWhenTheJobHashIsMissing()
    {
        var (mux, provider) = await ConnectAsync();
        var db = mux.GetDatabase();
        var ghostId = Guid.NewGuid().ToString();
        await db.HashSetAsync("nexjob:processing", ghostId, DateTimeOffset.UtcNow.AddHours(-1).ToString("O", System.Globalization.CultureInfo.InvariantCulture));

        await provider.RequeueOrphanedJobsAsync(TimeSpan.FromMinutes(1));

        (await db.HashExistsAsync("nexjob:processing", ghostId)).Should().BeFalse();
    }

    [Fact]
    public async Task RequeueOrphanedJobsAsync_SkipsAnUnparsableHeartbeat()
    {
        var (mux, provider) = await ConnectAsync();
        var db = mux.GetDatabase();
        var id = await CreateProcessingJobAsync(provider);
        await db.HashSetAsync("nexjob:processing", id.ToString(), "not-a-date");

        await provider.RequeueOrphanedJobsAsync(TimeSpan.Zero);

        ((string?)await db.HashGetAsync($"nexjob:jobs:{id}", "status")).Should().Be("Processing");
        (await db.HashExistsAsync("nexjob:processing", id.ToString())).Should().BeTrue("an entry that cannot be judged is left alone");
    }

    [Fact]
    public async Task RequeueOrphanScript_DoesNothingWhenTheHeartbeatWasRefreshedAfterTheScanRead()
    {
        var (mux, provider) = await ConnectAsync();
        var db = mux.GetDatabase();
        var id = await CreateProcessingJobAsync(provider);
        var refreshedHeartbeat = (string?)await db.HashGetAsync("nexjob:processing", id.ToString());

        // The scan read an older heartbeat ("stale"); the worker refreshed it before the script ran.
        var result = await RedisStorageProvider.RunRequeueOrphanScriptAsync(db, id.ToString(), "stale-heartbeat-read-by-the-scan", "default", 1);

        result.Should().Be(0);
        ((string?)await db.HashGetAsync($"nexjob:jobs:{id}", "status")).Should().Be("Processing", "a live worker's job must not be requeued");
        ((string?)await db.HashGetAsync("nexjob:processing", id.ToString())).Should().Be(refreshedHeartbeat);
    }

    private static async Task<Guid> CreateProcessingJobAsync(RedisStorageProvider provider)
    {
        var job = new JobRecord
        {
            Id = new JobId(Guid.NewGuid()),
            JobType = "T",
            InputType = "I",
            InputJson = "{}",
            Queue = "default",
            MaxAttempts = 3,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await provider.EnqueueAsync(job);
        var fetched = await provider.FetchNextAsync(["default"]);
        fetched.Should().NotBeNull();
        return job.Id.Value;
    }

    private async Task<(StackExchange.Redis.ConnectionMultiplexer Mux, RedisStorageProvider Provider)> ConnectAsync()
    {
        var mux = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(_fixture.Container.GetConnectionString());
        return (mux, new RedisStorageProvider(mux.GetDatabase()));
    }

    private static async Task<Guid> CreateSucceededJobAsync(RedisStorageProvider provider)
    {
        var job = new JobRecord
        {
            Id = new JobId(Guid.NewGuid()),
            JobType = "T",
            InputType = "I",
            InputJson = "{}",
            Queue = "default",
            MaxAttempts = 3,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await provider.EnqueueAsync(job);
        var fetched = (await provider.FetchNextAsync(["default"]))!;
        await provider.CommitJobResultAsync(fetched.Id, new JobExecutionResult { Succeeded = true, Logs = [] });
        return job.Id.Value;
    }
}
