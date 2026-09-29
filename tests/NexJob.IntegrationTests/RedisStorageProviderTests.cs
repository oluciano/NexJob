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
