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

    // ── Enqueue inserts hash and queue entry atomically (issue #239) ──────────

    private static JobRecord NewEnqueueJob(string queue = "default", string? key = null, JobStatus status = JobStatus.Enqueued, DateTimeOffset? scheduledAt = null, JobId? parent = null, JobPriority priority = JobPriority.Normal) => new()
    {
        Id = new JobId(Guid.NewGuid()),
        JobType = "T",
        InputType = "I",
        InputJson = "{}",
        Queue = queue,
        MaxAttempts = 3,
        CreatedAt = DateTimeOffset.UtcNow,
        IdempotencyKey = key,
        Status = status,
        ScheduledAt = scheduledAt,
        ParentJobId = parent,
        Priority = priority,
    };

    [Fact]
    public async Task EnqueueAsync_PutsTheJobInItsQueueExactlyOnce()
    {
        var (mux, provider) = await ConnectAsync();
        var db = mux.GetDatabase();
        var job = NewEnqueueJob();

        await provider.EnqueueAsync(job);

        (await db.SortedSetScoreAsync("nexjob:queue:default:z", job.Id.Value.ToString())).Should().NotBeNull();
        (await db.SortedSetLengthAsync("nexjob:queue:default:z")).Should().Be(1);
    }

    [Fact]
    public async Task EnqueueAsync_ScheduledJob_GoesToTheScheduledSetAndNotToTheQueue()
    {
        var (mux, provider) = await ConnectAsync();
        var db = mux.GetDatabase();
        var at = DateTimeOffset.UtcNow.AddHours(1);
        var job = NewEnqueueJob(status: JobStatus.Scheduled, scheduledAt: at);

        await provider.EnqueueAsync(job);

        var score = await db.SortedSetScoreAsync("nexjob:scheduled", job.Id.Value.ToString());
        score.Should().Be(at.ToUnixTimeMilliseconds());
        (await db.SortedSetLengthAsync("nexjob:queue:default:z")).Should().Be(0);
    }

    [Fact]
    public async Task EnqueueAsync_ContinuationJob_GoesToTheParentContinuationSet()
    {
        var (mux, provider) = await ConnectAsync();
        var db = mux.GetDatabase();
        var parent = Guid.NewGuid();
        var job = NewEnqueueJob(status: JobStatus.AwaitingContinuation, parent: new JobId(parent));

        await provider.EnqueueAsync(job);

        (await db.SetContainsAsync($"nexjob:continuations:{parent}", job.Id.Value.ToString())).Should().BeTrue();
        (await db.SortedSetLengthAsync("nexjob:queue:default:z")).Should().Be(0);
    }

    [Fact]
    public async Task EnqueueAsync_DuplicateKey_DoesNotCreateASecondQueueEntry()
    {
        var (mux, provider) = await ConnectAsync();
        var db = mux.GetDatabase();
        var key = $"k-{Guid.NewGuid()}";

        var first = NewEnqueueJob(key: key);
        await provider.EnqueueAsync(first, DuplicatePolicy.RejectAlways);
        var second = await provider.EnqueueAsync(NewEnqueueJob(key: key), DuplicatePolicy.RejectAlways);

        second.JobId.Should().Be(first.Id, "an active duplicate resolves to the existing job");
        (await db.SortedSetLengthAsync("nexjob:queue:default:z")).Should().Be(1);
    }

    [Fact]
    public async Task EnqueueAsync_QueueNameWithColonsAndUnicode_IsInsertedUnderThatQueue()
    {
        var (mux, provider) = await ConnectAsync();
        var db = mux.GetDatabase();
        var job = NewEnqueueJob(queue: "a:b:fila-ç");

        await provider.EnqueueAsync(job);

        (await db.SortedSetScoreAsync("nexjob:queue:a:b:fila-ç:z", job.Id.Value.ToString())).Should().NotBeNull();
    }

    // ── Idempotency key lives as long as the job (issue #238) ─────────────────

    private static async Task<JobId> EnqueueAndSucceedAsync(RedisStorageProvider provider, string key, bool purgeOnSuccess = false)
    {
        var job = NewEnqueueJob(key: key);
        await provider.EnqueueAsync(job, DuplicatePolicy.RejectAlways);
        var fetched = (await provider.FetchNextAsync(["default"]))!;
        await provider.CommitJobResultAsync(
            fetched.Id, new JobExecutionResult { Succeeded = true, Logs = [], PurgeOnSuccess = purgeOnSuccess });
        return job.Id;
    }

    [Fact]
    public async Task EnqueueAsync_IdempotencyKey_HasNoExpiry()
    {
        var (mux, provider) = await ConnectAsync();
        var db = mux.GetDatabase();
        var key = $"k-{Guid.NewGuid()}";

        await provider.EnqueueAsync(NewEnqueueJob(key: key), DuplicatePolicy.RejectAlways);

        (await db.KeyTimeToLiveAsync($"nexjob:idempotency:{key}")).Should().BeNull("RejectAlways promises exactly-once across the full job lifetime");
    }

    [Fact]
    public async Task PurgeJobsAsync_ReleasesTheIdempotencyKeyOfThePurgedJob()
    {
        var (mux, provider) = await ConnectAsync();
        var db = mux.GetDatabase();
        var key = $"k-{Guid.NewGuid()}";
        await EnqueueAndSucceedAsync(provider, key);
        await Task.Delay(200);

        (await provider.PurgeJobsAsync(new RetentionPolicy { RetainSucceeded = TimeSpan.FromMilliseconds(50) })).Should().Be(1);

        (await db.KeyExistsAsync($"nexjob:idempotency:{key}")).Should().BeFalse();
        var again = await provider.EnqueueAsync(NewEnqueueJob(key: key), DuplicatePolicy.RejectAlways);
        again.WasRejected.Should().BeFalse("the retained job is gone, so the key is free again");
    }

    [Fact]
    public async Task PurgeJobsAsync_KeepsTheIdempotencyKeyWhileTheJobIsRetained()
    {
        var (mux, provider) = await ConnectAsync();
        var key = $"k-{Guid.NewGuid()}";
        await EnqueueAndSucceedAsync(provider, key);

        await provider.PurgeJobsAsync(new RetentionPolicy { RetainSucceeded = TimeSpan.FromDays(7) });

        (await mux.GetDatabase().KeyExistsAsync($"nexjob:idempotency:{key}")).Should().BeTrue();
        var again = await provider.EnqueueAsync(NewEnqueueJob(key: key), DuplicatePolicy.RejectAlways);
        again.WasRejected.Should().BeTrue();
    }

    [Fact]
    public async Task PurgeJobsAsync_DoesNotReleaseAKeyOwnedByANewerJob()
    {
        var (mux, provider) = await ConnectAsync();
        var db = mux.GetDatabase();
        var key = $"k-{Guid.NewGuid()}";
        await EnqueueAndSucceedAsync(provider, key);
        var newerOwner = Guid.NewGuid().ToString();
        await db.StringSetAsync($"nexjob:idempotency:{key}", newerOwner);
        await Task.Delay(200);

        await provider.PurgeJobsAsync(new RetentionPolicy { RetainSucceeded = TimeSpan.FromMilliseconds(50) });

        ((string?)await db.StringGetAsync($"nexjob:idempotency:{key}")).Should().Be(newerOwner);
    }

    [Fact]
    public async Task DeleteJobAsync_ReleasesTheIdempotencyKey()
    {
        var (mux, provider) = await ConnectAsync();
        var key = $"k-{Guid.NewGuid()}";
        var job = NewEnqueueJob(key: key);
        await provider.EnqueueAsync(job, DuplicatePolicy.RejectAlways);

        await provider.DeleteJobAsync(job.Id);

        (await mux.GetDatabase().KeyExistsAsync($"nexjob:idempotency:{key}")).Should().BeFalse();
    }

    [Fact]
    public async Task CommitJobResultAsync_PurgeOnSuccess_ReleasesTheIdempotencyKey()
    {
        var (mux, provider) = await ConnectAsync();
        var key = $"k-{Guid.NewGuid()}";

        await EnqueueAndSucceedAsync(provider, key, purgeOnSuccess: true);

        (await mux.GetDatabase().KeyExistsAsync($"nexjob:idempotency:{key}")).Should().BeFalse();
    }

    [Fact]
    public async Task EnqueueAsync_WithoutIdempotencyKey_CreatesNoIdempotencyKey()
    {
        var (mux, provider) = await ConnectAsync();
        await provider.EnqueueAsync(NewEnqueueJob());

        var keys = mux.GetServer(mux.GetEndPoints()[0]).Keys(pattern: "nexjob:idempotency:*").ToArray();

        keys.Should().BeEmpty();
    }

    [Fact]
    public async Task EnqueueAsync_IdempotencyKeyWithSpecialCharacters_IsStoredAndRejectsDuplicates()
    {
        var (mux, provider) = await ConnectAsync();
        var key = $"ordem:{Guid.NewGuid()} çãé *[]? " + new string('x', 500);

        var first = NewEnqueueJob(key: key);
        await provider.EnqueueAsync(first, DuplicatePolicy.RejectAlways);
        var second = await provider.EnqueueAsync(NewEnqueueJob(key: key), DuplicatePolicy.RejectAlways);

        (await mux.GetDatabase().KeyExistsAsync($"nexjob:idempotency:{key}")).Should().BeTrue();
        second.JobId.Should().Be(first.Id);
    }

    // ── Released continuations are queued atomically on commit (issue #254) ───

    private static async Task<JobId> StartParentAsync(RedisStorageProvider provider, string queue = "default")
    {
        await provider.EnqueueAsync(NewEnqueueJob(queue));
        return (await provider.FetchNextAsync([queue]))!.Id;
    }

    private static Task CommitAsync(RedisStorageProvider provider, JobId id, bool succeeded) =>
        provider.CommitJobResultAsync(id, new JobExecutionResult
        {
            Succeeded = succeeded,
            Exception = succeeded ? null : new InvalidOperationException("boom"),
            Logs = [],
        });

    [Fact]
    public async Task CommitSuccess_ReleasesContinuation_ChildIsFetchable()
    {
        var (_, provider) = await ConnectAsync();
        var parentId = await StartParentAsync(provider);
        var child = NewEnqueueJob(status: JobStatus.AwaitingContinuation, parent: parentId);
        await provider.EnqueueAsync(child);

        await CommitAsync(provider, parentId, succeeded: true);

        var fetched = await provider.FetchNextAsync(["default"]);
        fetched.Should().NotBeNull("a released continuation must be reachable by the dispatcher");
        fetched!.Id.Should().Be(child.Id);
    }

    [Fact]
    public async Task CommitFailure_DoesNotReleaseContinuation()
    {
        var (mux, provider) = await ConnectAsync();
        var parentId = await StartParentAsync(provider);
        var child = NewEnqueueJob(status: JobStatus.AwaitingContinuation, parent: parentId);
        await provider.EnqueueAsync(child);

        await CommitAsync(provider, parentId, succeeded: false);

        var db = mux.GetDatabase();
        (await db.HashGetAsync($"nexjob:jobs:{child.Id.Value}", "status")).ToString().Should().Be("AwaitingContinuation");
        (await db.SortedSetScoreAsync("nexjob:queue:default:z", child.Id.Value.ToString())).Should().BeNull();
        (await provider.FetchNextAsync(["default"])).Should().BeNull();
    }

    [Fact]
    public async Task CommitSuccess_ManyChildren_NonDefaultQueueAndPriority_AllFetchable()
    {
        var (_, provider) = await ConnectAsync();
        var parentId = await StartParentAsync(provider, "reports");
        var children = Enumerable.Range(0, 3).Select(_ =>
        {
            return NewEnqueueJob("reports", status: JobStatus.AwaitingContinuation, parent: parentId, priority: JobPriority.High);
        }).ToList();
        foreach (var c in children)
        {
            await provider.EnqueueAsync(c);
        }

        await CommitAsync(provider, parentId, succeeded: true);

        var fetchedIds = new List<JobId>();
        for (var i = 0; i < 3; i++)
        {
            fetchedIds.Add((await provider.FetchNextAsync(["reports"]))!.Id);
        }

        fetchedIds.Should().BeEquivalentTo(children.Select(c => c.Id));
    }

    [Fact]
    public async Task LegacyChildWithoutQueueScore_IsStillQueued()
    {
        var (mux, provider) = await ConnectAsync();
        var parentId = await StartParentAsync(provider);
        var child = NewEnqueueJob(status: JobStatus.AwaitingContinuation, parent: parentId);
        await provider.EnqueueAsync(child);
        await mux.GetDatabase().HashDeleteAsync($"nexjob:jobs:{child.Id.Value}", "queueScore");

        await CommitAsync(provider, parentId, succeeded: true);

        (await provider.FetchNextAsync(["default"]))!.Id.Should().Be(child.Id);
    }

    [Fact]
    public async Task ChildNoLongerAwaiting_IsNotQueued()
    {
        var (mux, provider) = await ConnectAsync();
        var parentId = await StartParentAsync(provider);
        var child = NewEnqueueJob(status: JobStatus.AwaitingContinuation, parent: parentId);
        await provider.EnqueueAsync(child);
        var db = mux.GetDatabase();
        await db.HashSetAsync($"nexjob:jobs:{child.Id.Value}", "status", "Failed");

        await CommitAsync(provider, parentId, succeeded: true);

        (await db.SortedSetScoreAsync("nexjob:queue:default:z", child.Id.Value.ToString())).Should().BeNull();
        (await db.HashGetAsync($"nexjob:jobs:{child.Id.Value}", "status")).ToString().Should().Be("Failed");
    }

    // ── Due scheduled jobs are promoted atomically (issue #255) ───────────────

    private static JobRecord NewDueScheduledJob(string queue = "default", JobPriority priority = JobPriority.Normal) =>
        NewEnqueueJob(queue, status: JobStatus.Scheduled, scheduledAt: DateTimeOffset.UtcNow.AddSeconds(-1), priority: priority);

    [Fact]
    public async Task DueScheduledJob_IsPromotedOnceAndFetched()
    {
        var (mux, first) = await ConnectAsync();
        var second = new RedisStorageProvider((await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(_fixture.Container.GetConnectionString())).GetDatabase());
        var job = NewDueScheduledJob();
        await first.EnqueueAsync(job);

        var results = await Task.WhenAll(first.FetchNextAsync(["default"]), second.FetchNextAsync(["default"]));

        results.Count(r => r is not null).Should().Be(1, "two nodes promoting the same due job must not both run it");
        results.Single(r => r is not null)!.Id.Should().Be(job.Id);
        (await mux.GetDatabase().SortedSetScoreAsync("nexjob:scheduled", job.Id.Value.ToString())).Should().BeNull();
    }

    [Fact]
    public async Task ScheduledEntryForJobAlreadyProcessing_IsDroppedNotRequeued()
    {
        var (mux, provider) = await ConnectAsync();
        var db = mux.GetDatabase();
        await provider.EnqueueAsync(NewEnqueueJob());
        var processing = (await provider.FetchNextAsync(["default"]))!;
        await db.SortedSetAddAsync("nexjob:scheduled", processing.Id.Value.ToString(), DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds());

        var fetched = await provider.FetchNextAsync(["default"]);

        fetched.Should().BeNull("a job that is already running must not be requeued by a stale scheduled entry");
        (await db.SortedSetScoreAsync("nexjob:scheduled", processing.Id.Value.ToString())).Should().BeNull("the stale entry is dropped");
        (await db.HashGetAsync($"nexjob:jobs:{processing.Id.Value}", "status")).ToString().Should().Be("Processing");
    }

    [Fact]
    public async Task MissingHash_EntryRemoved()
    {
        var (mux, provider) = await ConnectAsync();
        var db = mux.GetDatabase();
        var ghost = Guid.NewGuid().ToString();
        await db.SortedSetAddAsync("nexjob:scheduled", ghost, DateTimeOffset.UtcNow.AddSeconds(-1).ToUnixTimeMilliseconds());

        (await provider.FetchNextAsync(["default"])).Should().BeNull();

        (await db.SortedSetScoreAsync("nexjob:scheduled", ghost)).Should().BeNull();
    }

    [Fact]
    public async Task MoreThanLimitDue_AllPromotedAcrossCalls()
    {
        var (_, provider) = await ConnectAsync();
        for (var i = 0; i < 250; i++)
        {
            await provider.EnqueueAsync(NewDueScheduledJob());
        }

        var fetched = 0;
        while (await provider.FetchNextAsync(["default"]) is not null)
        {
            fetched++;
        }

        fetched.Should().Be(250, "a backlog larger than one promotion batch must fully drain");
    }

    [Fact]
    public async Task NonDefaultQueueAndPriority_KeepOrdering()
    {
        var (_, provider) = await ConnectAsync();
        var low = NewDueScheduledJob("q", JobPriority.Low);
        var critical = NewDueScheduledJob("q", JobPriority.Critical);
        await provider.EnqueueAsync(low);
        await provider.EnqueueAsync(critical);

        (await provider.FetchNextAsync(["q"]))!.Id.Should().Be(critical.Id);
        (await provider.FetchNextAsync(["q"]))!.Id.Should().Be(low.Id);
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
