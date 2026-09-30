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

    // ── Non-UTC offsets must not change scheduling (issue #263) ───────────────

    private static JobRecord NewScheduledJob(DateTimeOffset scheduledAt) => new()
    {
        Id = new JobId(Guid.NewGuid()),
        JobType = "T",
        InputType = "I",
        InputJson = "{}",
        Queue = "default",
        MaxAttempts = 3,
        CreatedAt = DateTimeOffset.UtcNow,
        Status = JobStatus.Scheduled,
        ScheduledAt = scheduledAt,
    };

    [Fact]
    public async Task ScheduleAt_NonUtcOffset_RunsAtCorrectInstant()
    {
        var (_, _, _, provider) = await CreateStorageAsync();
        await provider.EnqueueAsync(NewScheduledJob(DateTimeOffset.UtcNow.AddHours(1).ToOffset(TimeSpan.FromHours(-3))));

        (await provider.FetchNextAsync(["default"])).Should().BeNull("one hour ahead is one hour ahead in any offset");
    }

    [Fact]
    public async Task ScheduleAt_PositiveOffset_NotDelayed()
    {
        var (_, _, _, provider) = await CreateStorageAsync();
        var job = NewScheduledJob(DateTimeOffset.UtcNow.AddMinutes(-1).ToOffset(TimeSpan.FromHours(9)));
        await provider.EnqueueAsync(job);

        var fetched = await provider.FetchNextAsync(["default"]);

        fetched.Should().NotBeNull("a job that is already due must run regardless of the offset it was scheduled with");
        fetched!.Id.Should().Be(job.Id);
    }

    [Fact]
    public async Task RetryAt_NonUtcOffset_IsNotPromotedEarly()
    {
        var (_, _, _, provider) = await CreateStorageAsync();
        await provider.EnqueueAsync(NewScheduledJob(DateTimeOffset.UtcNow.AddMinutes(-1)));
        var running = (await provider.FetchNextAsync(["default"]))!;

        await provider.SetFailedAsync(running.Id, new InvalidOperationException("boom"), DateTimeOffset.UtcNow.AddHours(1).ToOffset(TimeSpan.FromHours(-3)));

        (await provider.FetchNextAsync(["default"])).Should().BeNull();
    }

    [Fact]
    public async Task DateTimeOffsetMinMax_RoundTrip()
    {
        var (_, _, dashboard, provider) = await CreateStorageAsync();
        var job = NewScheduledJob(DateTimeOffset.MaxValue);
        await provider.EnqueueAsync(job);

        var stored = await dashboard.GetJobByIdAsync(job.Id);

        stored!.ScheduledAt.Should().Be(DateTimeOffset.MaxValue);
        (await provider.FetchNextAsync(["default"])).Should().BeNull("a job scheduled at the maximum instant never becomes due");
    }

    [Fact]
    public async Task CreatedAt_NonUtcOffset_KeepsTheSameInstant()
    {
        var (_, _, dashboard, provider) = await CreateStorageAsync();
        var createdAt = new DateTimeOffset(2026, 3, 1, 10, 30, 0, TimeSpan.FromHours(-3));
        var withOffset = new JobRecord
        {
            Id = new JobId(Guid.NewGuid()),
            JobType = "T",
            InputType = "I",
            InputJson = "{}",
            Queue = "default",
            MaxAttempts = 3,
            CreatedAt = createdAt,
            Status = JobStatus.Scheduled,
            ScheduledAt = DateTimeOffset.UtcNow.AddHours(1),
        };
        await provider.EnqueueAsync(withOffset);

        (await dashboard.GetJobByIdAsync(withOffset.Id))!.CreatedAt.Should().Be(createdAt);
    }

    [Fact]
    public async Task RecurringNextExecution_NonUtcOffset_DueOnlyWhenTheInstantHasPassed()
    {
        var (_, recurring, _, _) = await CreateStorageAsync();
        await recurring.UpsertRecurringJobAsync(NewRecurring("due", DateTimeOffset.UtcNow.AddMinutes(-1).ToOffset(TimeSpan.FromHours(9))));
        await recurring.UpsertRecurringJobAsync(NewRecurring("later", DateTimeOffset.UtcNow.AddHours(1).ToOffset(TimeSpan.FromHours(-3))));

        var due = await recurring.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow);

        due.Select(r => r.RecurringJobId).Should().BeEquivalentTo("due");
    }

    private static RecurringJobRecord NewRecurring(string id, DateTimeOffset nextExecution) => new()
    {
        RecurringJobId = id,
        JobType = "T",
        InputType = "I",
        InputJson = "{}",
        Cron = "* * * * *",
        Queue = "default",
        NextExecution = nextExecution,
        CreatedAt = DateTimeOffset.UtcNow,
    };

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
