using System.Globalization;
using Microsoft.Extensions.Logging;
using NexJob.Redis;
using NexJob.Storage;
using StackExchange.Redis;
using Xunit;

namespace NexJob.IntegrationTests;

/// <summary>
/// Issue #286: the Redis job index and the Succeeded/Failed sets were built once, guarded by a marker. A node on an
/// older version that kept writing after the marker was set left jobs out of the index for good (missing from the
/// dashboard, never purged), and the first backfill had no lock. The index is now reconciled periodically under a lock.
/// </summary>
public sealed class RedisIndexReconciliationTests : IClassFixture<RedisFixture>
{
    private const string IndexKey = "nexjob:index:all";
    private const string ReadyKey = "nexjob:index:ready";
    private const string LockKey = "nexjob:index:lock";
    private const string SucceededKey = "nexjob:status:Succeeded";

    private readonly RedisFixture _fixture;

    public RedisIndexReconciliationTests(RedisFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task JobWrittenByAnOldNodeAfterTheMarker_IsIndexedAndPurgeable()
    {
        // N1 (Positive): a job that never reached the index (written by a v5.5.0 node) is picked up and can be purged.
        var (db, provider, log) = await ConnectAsync();
        var id = await SucceedOneJobAsync(provider);
        await ForgetInIndexAsync(db, id);
        await SetMarkerAsync(db, DateTimeOffset.UtcNow.AddHours(-2));

        var page = await provider.GetJobsAsync(new JobFilter(), 1, 50);

        Assert.Contains(page.Items, j => j.Id.Value == id);
        Assert.NotNull(await db.SortedSetScoreAsync(SucceededKey, id.ToString()));
        Assert.True(DateTimeOffset.UtcNow - await ReadMarkerAsync(db) < TimeSpan.FromMinutes(1));
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("missing from the Redis job index", StringComparison.Ordinal));

        await Task.Delay(300);
        var purged = await provider.PurgeJobsAsync(new RetentionPolicy { RetainSucceeded = TimeSpan.FromMilliseconds(100), });
        Assert.Equal(1, purged);
    }

    [Fact]
    public async Task FreshMarker_DoesNotScanAgain()
    {
        // N1: inside the interval the marker is trusted, so reads stay cheap and nodes do not stampede.
        var (db, provider, _) = await ConnectAsync();
        var id = await SucceedOneJobAsync(provider);
        await ForgetInIndexAsync(db, id);
        await SetMarkerAsync(db, DateTimeOffset.UtcNow);

        var page = await provider.GetJobsAsync(new JobFilter(), 1, 50);

        Assert.DoesNotContain(page.Items, j => j.Id.Value == id);
    }

    [Fact]
    public async Task HeldLock_MakesTheCallerSkipWithoutError()
    {
        // N2 (Negative): another node is reconciling, so this one skips, does not fail and leaves the marker alone.
        var (db, provider, _) = await ConnectAsync();
        var id = await SucceedOneJobAsync(provider);
        await ForgetInIndexAsync(db, id);
        var old = DateTimeOffset.UtcNow.AddHours(-2);
        await SetMarkerAsync(db, old);
        await db.StringSetAsync(LockKey, "another-node", TimeSpan.FromMinutes(5), When.NotExists);

        var page = await provider.GetJobsAsync(new JobFilter(), 1, 50);

        Assert.DoesNotContain(page.Items, j => j.Id.Value == id);
        Assert.Equal(old.ToUnixTimeMilliseconds(), (await ReadMarkerAsync(db)).ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task StaleLock_Expires_AndTheNextCallReconciles()
    {
        // N3: a lock left by a crashed node ends by itself.
        var (db, provider, _) = await ConnectAsync();
        var id = await SucceedOneJobAsync(provider);
        await ForgetInIndexAsync(db, id);
        await SetMarkerAsync(db, DateTimeOffset.UtcNow.AddHours(-2));
        await db.StringSetAsync(LockKey, "crashed-node", TimeSpan.FromMilliseconds(300), When.NotExists);
        await Task.Delay(700);

        var page = await provider.GetJobsAsync(new JobFilter(), 1, 50);

        Assert.Contains(page.Items, j => j.Id.Value == id);
    }

    [Fact]
    public async Task EmptyKeyspace_AdvancesTheMarker_AndReturnsAnEmptyPage()
    {
        // N3 (boundary): nothing to index is not an error, and the lock does not stay behind.
        var (db, provider, log) = await ConnectAsync();

        var page = await provider.GetJobsAsync(new JobFilter(), 1, 50);

        Assert.Empty(page.Items);
        Assert.True(DateTimeOffset.UtcNow - await ReadMarkerAsync(db) < TimeSpan.FromMinutes(1));
        Assert.False(await db.KeyExistsAsync(LockKey));
        Assert.DoesNotContain(log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task MoreJobsThanOneChunk_AreAllIndexed()
    {
        // N3 (boundary): the backfill works in chunks of 500, so 1200 jobs cross the boundary twice.
        var (db, provider, _) = await ConnectAsync();
        for (var i = 0; i < 1200; i++)
        {
            await provider.EnqueueAsync(NewJob());
        }

        await db.KeyDeleteAsync(IndexKey);
        await SetMarkerAsync(db, DateTimeOffset.UtcNow.AddHours(-2));

        var page = await provider.GetJobsAsync(new JobFilter(), 1, 10);

        Assert.Equal(1200, page.TotalCount);
    }

    [Fact]
    public async Task FailedReconciliation_DoesNotAdvanceTheMarker_ReleasesTheLock_AndIsRetried()
    {
        // N2: a failure is not remembered as success. The next call tries again, and the lock is not left behind.
        var (db, provider, log) = await ConnectAsync();
        var id = await SucceedOneJobAsync(provider);
        await ForgetInIndexAsync(db, id);
        var old = DateTimeOffset.UtcNow.AddHours(-2);
        await SetMarkerAsync(db, old);
        await db.KeyDeleteAsync(SucceededKey);
        await db.StringSetAsync(SucceededKey, "not-a-sorted-set");

        var page = await provider.GetJobsAsync(new JobFilter(), 1, 50);

        Assert.NotNull(page);
        Assert.Equal(old.ToUnixTimeMilliseconds(), (await ReadMarkerAsync(db)).ToUnixTimeMilliseconds());
        Assert.False(await db.KeyExistsAsync(LockKey));
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("failed", StringComparison.Ordinal));

        await db.KeyDeleteAsync(SucceededKey);
        var retried = await provider.GetJobsAsync(new JobFilter(), 1, 50);

        Assert.Contains(retried.Items, j => j.Id.Value == id);
    }

    [Fact]
    public async Task SeveralNodesCallingAtOnce_OnlyOneReconciles()
    {
        // N1 (Positive): the lock keeps a stampede away. Six nodes ask at the same time and exactly one scans.
        var (db, provider, _) = await ConnectAsync();
        var id = await SucceedOneJobAsync(provider);
        await ForgetInIndexAsync(db, id);
        await SetMarkerAsync(db, DateTimeOffset.UtcNow.AddHours(-2));
        var nodes = Enumerable.Range(0, 6).Select(_ =>
        {
            var log = new ListLog();
            return (Provider: new RedisStorageProvider(db, log), Log: log);
        }).ToList();

        await Task.WhenAll(nodes.Select(n => Task.Run(() => n.Provider.GetJobsAsync(new JobFilter(), 1, 50))));

        var reports = nodes.SelectMany(n => n.Log.Entries).Count(e => e.Level == LogLevel.Warning && e.Message.Contains("missing from the Redis job index", StringComparison.Ordinal));
        Assert.Equal(1, reports);
        Assert.False(await db.KeyExistsAsync(LockKey));
    }

    [Fact]
    public async Task IndexJobsIfTheyExist_SkipsAJobWhoseHashIsGone()
    {
        // N3 (purge during the scan): a job purged after it was read must not be added back to the index.
        var (db, provider, _) = await ConnectAsync();
        var live = await SucceedOneJobAsync(provider);
        await ForgetInIndexAsync(db, live);
        var gone = Guid.NewGuid();

        var added = await provider.IndexJobsIfTheyExistAsync(
        [
            (live.ToString(), 1_000, "Succeeded", 2_000),
            (gone.ToString(), 1_000, "Succeeded", 2_000),
        ]);

        Assert.Equal(1, added);
        Assert.NotNull(await db.SortedSetScoreAsync(IndexKey, live.ToString()));
        Assert.Null(await db.SortedSetScoreAsync(IndexKey, gone.ToString()));
        Assert.Null(await db.SortedSetScoreAsync(SucceededKey, gone.ToString()));
    }

    private static JobRecord NewJob() => new()
    {
        Id = new JobId(Guid.NewGuid()),
        JobType = "T",
        InputType = "I",
        InputJson = "{}",
        Queue = "default",
        MaxAttempts = 3,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static async Task<Guid> SucceedOneJobAsync(RedisStorageProvider provider)
    {
        var job = NewJob();
        await provider.EnqueueAsync(job);
        var fetched = await provider.FetchNextAsync(["default"]);
        Assert.NotNull(fetched);
        await provider.CommitJobResultAsync(job.Id, new JobExecutionResult { Succeeded = true, Logs = [], });
        return job.Id.Value;
    }

    // What a node on v5.5.0 leaves behind: the job exists, but it is in neither the index nor the status set.
    private static async Task ForgetInIndexAsync(IDatabase db, Guid id)
    {
        await db.SortedSetRemoveAsync(IndexKey, id.ToString());
        await db.SortedSetRemoveAsync(SucceededKey, id.ToString());
    }

    private static Task SetMarkerAsync(IDatabase db, DateTimeOffset at) =>
        db.StringSetAsync(ReadyKey, at.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));

    private static async Task<DateTimeOffset> ReadMarkerAsync(IDatabase db)
    {
        var raw = await db.StringGetAsync(ReadyKey);
        return DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(raw.ToString(), CultureInfo.InvariantCulture));
    }

    private async Task<(IDatabase Db, RedisStorageProvider Provider, ListLog Log)> ConnectAsync()
    {
        var mux = await ConnectionMultiplexer.ConnectAsync(_fixture.Container.GetConnectionString() + ",allowAdmin=true");
        var db = mux.GetDatabase();
        await mux.GetServer(mux.GetEndPoints()[0]).FlushDatabaseAsync();
        var log = new ListLog();
        return (db, new RedisStorageProvider(db, log), log);
    }

    private sealed class ListLog : ILogger<RedisStorageProvider>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
            {
                _entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
