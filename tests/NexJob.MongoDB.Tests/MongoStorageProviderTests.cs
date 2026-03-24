using FluentAssertions;
using MongoDB.Driver;
using NexJob;
using NexJob.MongoDB;
using Xunit;

namespace NexJob.MongoDB.Tests;

/// <summary>
/// Integration tests for <see cref="MongoStorageProvider"/>.
/// Requires a running MongoDB instance at mongodb://localhost:27017.
/// Each test class gets a fresh database (dropped in Dispose).
/// </summary>
[Collection("MongoDB")]
public sealed class MongoStorageProviderTests : IAsyncLifetime
{
    private readonly IMongoDatabase _database;
    private readonly MongoStorageProvider _sut;
    private readonly string _dbName;

    public MongoStorageProviderTests()
    {
        _dbName   = $"nexjob_tests_{Guid.NewGuid():N}";
        var client = new MongoClient(MongoTestFixture.ConnectionString);
        _database  = client.GetDatabase(_dbName);
        _sut       = new MongoStorageProvider(_database);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() =>
        await _database.Client.DropDatabaseAsync(_dbName);

    // ── EnqueueAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_StoresJobAndReturnsItsId()
    {
        var job = MakeJob();
        var returned = await _sut.EnqueueAsync(job);
        returned.Should().Be(job.Id);
    }

    [Fact]
    public async Task EnqueueAsync_WithIdempotencyKey_ReturnsSameIdForDuplicate()
    {
        var job1 = MakeJob(idempotencyKey: "order-42");
        var job2 = MakeJob(idempotencyKey: "order-42");

        var id1 = await _sut.EnqueueAsync(job1);
        var id2 = await _sut.EnqueueAsync(job2);

        id2.Should().Be(id1, "duplicate idempotency key must return the original job id");
    }

    [Fact]
    public async Task EnqueueAsync_WithIdempotencyKey_AllowsNewJobAfterPreviousSucceeded()
    {
        var job1 = MakeJob(idempotencyKey: "key-1");
        await _sut.EnqueueAsync(job1);

        var fetched = await _sut.FetchNextAsync(["default"]);
        await _sut.AcknowledgeAsync(fetched!.Id);

        var job2 = MakeJob(idempotencyKey: "key-1");
        var id2  = await _sut.EnqueueAsync(job2);

        id2.Should().Be(job2.Id, "previous job succeeded so a new one should be accepted");
    }

    // ── FetchNextAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task FetchNextAsync_ReturnsEnqueuedJob()
    {
        var job = MakeJob();
        await _sut.EnqueueAsync(job);

        var fetched = await _sut.FetchNextAsync(["default"]);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(job.Id);
    }

    [Fact]
    public async Task FetchNextAsync_SetsStatusToProcessing()
    {
        await _sut.EnqueueAsync(MakeJob());
        var fetched = await _sut.FetchNextAsync(["default"]);
        fetched!.Status.Should().Be(JobStatus.Processing);
    }

    [Fact]
    public async Task FetchNextAsync_IncrementsAttemptCount()
    {
        await _sut.EnqueueAsync(MakeJob());
        var fetched = await _sut.FetchNextAsync(["default"]);
        fetched!.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task FetchNextAsync_ReturnsNullWhenQueueIsEmpty()
    {
        var result = await _sut.FetchNextAsync(["empty-queue"]);
        result.Should().BeNull();
    }

    [Fact]
    public async Task FetchNextAsync_RespectsPriorityOrder()
    {
        await _sut.EnqueueAsync(MakeJob(priority: JobPriority.Normal));
        await _sut.EnqueueAsync(MakeJob(priority: JobPriority.Low));
        await _sut.EnqueueAsync(MakeJob(priority: JobPriority.Critical));
        await _sut.EnqueueAsync(MakeJob(priority: JobPriority.High));

        var first  = await _sut.FetchNextAsync(["default"]);
        var second = await _sut.FetchNextAsync(["default"]);
        var third  = await _sut.FetchNextAsync(["default"]);
        var fourth = await _sut.FetchNextAsync(["default"]);

        first!.Priority.Should().Be(JobPriority.Critical);
        second!.Priority.Should().Be(JobPriority.High);
        third!.Priority.Should().Be(JobPriority.Normal);
        fourth!.Priority.Should().Be(JobPriority.Low);
    }

    [Fact]
    public async Task FetchNextAsync_DoesNotReturnSameJobTwice()
    {
        await _sut.EnqueueAsync(MakeJob());

        var first  = await _sut.FetchNextAsync(["default"]);
        var second = await _sut.FetchNextAsync(["default"]);

        first.Should().NotBeNull();
        second.Should().BeNull("the job is already Processing");
    }

    // ── AcknowledgeAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task AcknowledgeAsync_SetsStatusToSucceeded()
    {
        await _sut.EnqueueAsync(MakeJob());
        var fetched = await _sut.FetchNextAsync(["default"]);

        await _sut.AcknowledgeAsync(fetched!.Id);

        var doc = await GetJobAsync(fetched.Id);
        doc!.Status.Should().Be(JobStatus.Succeeded);
        doc.CompletedAt.Should().NotBeNull();
    }

    // ── SetFailedAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task SetFailedAsync_WithRetryAt_SchedulesRetry()
    {
        await _sut.EnqueueAsync(MakeJob());
        var fetched = await _sut.FetchNextAsync(["default"]);

        var retryAt = DateTimeOffset.UtcNow.AddSeconds(60);
        await _sut.SetFailedAsync(fetched!.Id, new Exception("transient"), retryAt);

        var doc = await GetJobAsync(fetched.Id);
        doc!.Status.Should().Be(JobStatus.Scheduled);
        doc.RetryAt.Should().BeCloseTo(retryAt, TimeSpan.FromSeconds(2));
        doc.LastErrorMessage.Should().Be("transient");
    }

    [Fact]
    public async Task SetFailedAsync_WithNullRetryAt_MovesToDeadLetter()
    {
        await _sut.EnqueueAsync(MakeJob());
        var fetched = await _sut.FetchNextAsync(["default"]);

        await _sut.SetFailedAsync(fetched!.Id, new Exception("fatal"), retryAt: null);

        var doc = await GetJobAsync(fetched.Id);
        doc!.Status.Should().Be(JobStatus.Failed);
        doc.CompletedAt.Should().NotBeNull();
    }

    // ── UpdateHeartbeatAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task UpdateHeartbeatAsync_RefreshesTimestamp()
    {
        await _sut.EnqueueAsync(MakeJob());
        var fetched = await _sut.FetchNextAsync(["default"]);
        var original = (await GetJobAsync(fetched!.Id))!.HeartbeatAt;

        await Task.Delay(20);
        await _sut.UpdateHeartbeatAsync(fetched.Id);

        var updated = (await GetJobAsync(fetched.Id))!.HeartbeatAt;
        updated.Should().BeAfter(original!.Value);
    }

    // ── RequeueOrphanedJobsAsync ──────────────────────────────────────────────

    [Fact]
    public async Task RequeueOrphanedJobsAsync_RequeuesJobWithExpiredHeartbeat()
    {
        await _sut.EnqueueAsync(MakeJob());
        var fetched = await _sut.FetchNextAsync(["default"]);

        // Backdate the heartbeat directly in Mongo
        await BackdateHeartbeatAsync(fetched!.Id, DateTimeOffset.UtcNow.AddMinutes(-10));

        await _sut.RequeueOrphanedJobsAsync(TimeSpan.FromMinutes(5));

        var doc = await GetJobAsync(fetched.Id);
        doc!.Status.Should().Be(JobStatus.Enqueued);
        doc.HeartbeatAt.Should().BeNull();
    }

    [Fact]
    public async Task RequeueOrphanedJobsAsync_DoesNotRequeueActiveJob()
    {
        await _sut.EnqueueAsync(MakeJob());
        var fetched = await _sut.FetchNextAsync(["default"]);

        await _sut.UpdateHeartbeatAsync(fetched!.Id); // fresh heartbeat

        await _sut.RequeueOrphanedJobsAsync(TimeSpan.FromMinutes(5));

        var doc = await GetJobAsync(fetched.Id);
        doc!.Status.Should().Be(JobStatus.Processing);
    }

    // ── EnqueueContinuationsAsync ─────────────────────────────────────────────

    [Fact]
    public async Task EnqueueContinuationsAsync_ActivatesContinuationJob()
    {
        var parent = MakeJob();
        await _sut.EnqueueAsync(parent);

        var continuation = new JobRecord
        {
            Id           = JobId.New(),
            JobType      = "StubJob",
            InputType    = "System.String",
            InputJson    = "\"cont\"",
            Queue        = "default",
            Priority     = JobPriority.Normal,
            Status       = JobStatus.AwaitingContinuation,
            ParentJobId  = parent.Id,
            CreatedAt    = DateTimeOffset.UtcNow,
            MaxAttempts  = 10,
        };
        await _sut.EnqueueAsync(continuation);

        var parentFetched = await _sut.FetchNextAsync(["default"]);
        await _sut.AcknowledgeAsync(parentFetched!.Id);
        await _sut.EnqueueContinuationsAsync(parentFetched.Id);

        var contFetched = await _sut.FetchNextAsync(["default"]);
        contFetched.Should().NotBeNull();
        contFetched!.Id.Should().Be(continuation.Id);
    }

    // ── Recurring jobs ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetDueRecurringJobsAsync_ReturnsDueJobs()
    {
        var r = MakeRecurring("nightly", DateTimeOffset.UtcNow.AddMinutes(-1));
        await _sut.UpsertRecurringJobAsync(r);

        var due = await _sut.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow);
        due.Should().ContainSingle(x => x.RecurringJobId == "nightly");
    }

    [Fact]
    public async Task GetDueRecurringJobsAsync_DoesNotReturnFutureJobs()
    {
        var r = MakeRecurring("future", DateTimeOffset.UtcNow.AddHours(1));
        await _sut.UpsertRecurringJobAsync(r);

        var due = await _sut.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow);
        due.Should().NotContain(x => x.RecurringJobId == "future");
    }

    [Fact]
    public async Task UpsertRecurringJobAsync_UpdatesExistingRecord()
    {
        var r1 = MakeRecurring("job", DateTimeOffset.UtcNow.AddHours(1));
        await _sut.UpsertRecurringJobAsync(r1);

        var r2 = new RecurringJobRecord
        {
            RecurringJobId = r1.RecurringJobId,
            JobType        = r1.JobType,
            InputType      = r1.InputType,
            InputJson      = r1.InputJson,
            Cron           = r1.Cron,
            Queue          = r1.Queue,
            CreatedAt      = r1.CreatedAt,
            NextExecution  = DateTimeOffset.UtcNow.AddMinutes(-1),
        };
        await _sut.UpsertRecurringJobAsync(r2);

        var due = await _sut.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow);
        due.Should().ContainSingle(x => x.RecurringJobId == "job");
    }

    [Fact]
    public async Task DeleteRecurringJobAsync_RemovesTheRecord()
    {
        await _sut.UpsertRecurringJobAsync(MakeRecurring("to-delete", DateTimeOffset.UtcNow.AddMinutes(-1)));
        await _sut.DeleteRecurringJobAsync("to-delete");

        var due = await _sut.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow.AddYears(1));
        due.Should().NotContain(x => x.RecurringJobId == "to-delete");
    }

    [Fact]
    public async Task SetRecurringJobNextExecutionAsync_UpdatesNextExecution()
    {
        await _sut.UpsertRecurringJobAsync(MakeRecurring("r1", DateTimeOffset.UtcNow.AddMinutes(-1)));

        var next = DateTimeOffset.UtcNow.AddHours(1);
        await _sut.SetRecurringJobNextExecutionAsync("r1", next);

        var due = await _sut.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow); // now < next
        due.Should().NotContain(x => x.RecurringJobId == "r1");
    }

    // ── Scheduled job promotion ───────────────────────────────────────────────

    [Fact]
    public async Task FetchNextAsync_PromotesDueScheduledJob()
    {
        var job = new JobRecord
        {
            Id          = JobId.New(),
            JobType     = "StubJob",
            InputType   = "System.String",
            InputJson   = "\"test\"",
            Queue       = "default",
            Priority    = JobPriority.Normal,
            Status      = JobStatus.Scheduled,
            ScheduledAt = DateTimeOffset.UtcNow.AddMilliseconds(-1),
            CreatedAt   = DateTimeOffset.UtcNow,
            MaxAttempts = 10,
        };
        await _sut.EnqueueAsync(job);

        var fetched = await _sut.FetchNextAsync(["default"]);
        fetched.Should().NotBeNull("scheduled job that is due should be promoted and returned");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static JobRecord MakeJob(
        JobPriority priority = JobPriority.Normal,
        string? idempotencyKey = null) => new()
    {
        Id             = JobId.New(),
        JobType        = "StubJob",
        InputType      = "System.String",
        InputJson      = "\"test\"",
        Queue          = "default",
        Priority       = priority,
        Status         = JobStatus.Enqueued,
        IdempotencyKey = idempotencyKey,
        CreatedAt      = DateTimeOffset.UtcNow,
        MaxAttempts    = 10,
    };

    private static RecurringJobRecord MakeRecurring(string id, DateTimeOffset nextExecution) => new()
    {
        RecurringJobId = id,
        JobType        = "StubJob",
        InputType      = "System.String",
        InputJson      = "\"go\"",
        Cron           = "0 * * * *",
        Queue          = "default",
        NextExecution  = nextExecution,
        CreatedAt      = DateTimeOffset.UtcNow,
    };

    private async Task<JobRecord?> GetJobAsync(JobId id)
    {
        var col = _database.GetCollection<JobDocument>("nexjob_jobs");
        var doc = await col.Find(Builders<JobDocument>.Filter.Eq(d => d.Id, id))
                           .FirstOrDefaultAsync();
        return doc?.ToRecord();
    }

    private async Task BackdateHeartbeatAsync(JobId id, DateTimeOffset heartbeat)
    {
        var col    = _database.GetCollection<JobDocument>("nexjob_jobs");
        var update = Builders<JobDocument>.Update.Set(d => d.HeartbeatAt, heartbeat);
        await col.UpdateOneAsync(Builders<JobDocument>.Filter.Eq(d => d.Id, id), update);
    }
}
