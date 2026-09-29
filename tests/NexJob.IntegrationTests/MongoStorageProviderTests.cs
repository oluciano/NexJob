using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using NexJob.MongoDB;
using NexJob.Storage;
using Testcontainers.MongoDb;
using Xunit;

namespace NexJob.IntegrationTests;

/// <summary>
/// Runs the full <see cref="StorageProviderTestsBase"/> contract against a real
/// MongoDB instance spun up via Testcontainers.
/// Requires Docker to be available on the host.
/// </summary>
public sealed class MongoStorageProviderTests : StorageProviderTestsBase, IClassFixture<MongoFixture>, IAsyncLifetime
{
    private readonly MongoFixture _fixture;
    private readonly MongoTestDatabases _databases = new();

    public MongoStorageProviderTests(MongoFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _databases.DisposeAsync();

    protected override async Task<(IJobStorage Job, IRecurringStorage Recurring, IDashboardStorage Dashboard, IStorageProvider Full)> CreateStorageAsync()
    {
        // Create a unique database for each test — ensures complete isolation like PostgreSQL tests.
        // It is dropped when the test finishes (see DisposeAsync).
        var database = _databases.Create(_fixture.Container.GetConnectionString(), "nexjob_test");

        var provider = new MongoStorageProvider(database);

        // Wait until the idempotency index is confirmed present
        // Necessary in CI environments where index propagation is slower
        var collection = database.GetCollection<BsonDocument>("nexjob_jobs");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            using (var indexCursor = await collection.Indexes.ListAsync())
            {
                var indexList = await indexCursor.ToListAsync();
                if (indexList.FindIndex(i => i["name"] == "idempotency_key") >= 0)
                {
                    break;
                }
            }

            await Task.Delay(25);
        }

        return (provider, provider, provider, provider);
    }

    // ── Upgrade from the broad idempotency index (issue #234) ─────────────────

    [Fact]
    public async Task Existing_broad_idempotency_index_is_replaced_and_finished_jobs_no_longer_block_the_key()
    {
        var database = _databases.Create(_fixture.Container.GetConnectionString(), "nexjob_test");

        // The index older versions created: unique over every job that has a key, whatever its status.
        await database.RunCommandAsync<BsonDocument>(new BsonDocument
        {
            { "createIndexes", "nexjob_jobs" },
            {
                "indexes", new BsonArray
                {
                    new BsonDocument
                    {
                        { "key", new BsonDocument { { "IdempotencyKey", 1 } } },
                        { "name", "idempotency_key" },
                        { "unique", true },
                        { "partialFilterExpression", new BsonDocument { { "IdempotencyKey", new BsonDocument { { "$type", "string" } } } } },
                    },
                }
            },
        });

        var provider = new MongoStorageProvider(database);

        var key = $"upgrade-{Guid.NewGuid()}";
        var first = NewKeyedJob(key);
        await provider.EnqueueAsync(first, DuplicatePolicy.AllowAfterFailed);
        var fetched = (await provider.FetchNextAsync(["default"]))!;
        await provider.CommitJobResultAsync(fetched.Id, new JobExecutionResult { Succeeded = true, Logs = [] });

        var second = NewKeyedJob(key);
        var result = await provider.EnqueueAsync(second, DuplicatePolicy.AllowAfterFailed);

        result.JobId.Should().Be(second.Id, "the upgraded index must not be blocked by the finished job");
    }

    private static JobRecord NewKeyedJob(string key) => new()
    {
        Id = new JobId(Guid.NewGuid()),
        JobType = "T",
        InputType = "I",
        InputJson = "{}",
        Queue = "default",
        MaxAttempts = 3,
        CreatedAt = DateTimeOffset.UtcNow,
        IdempotencyKey = key,
    };
}
