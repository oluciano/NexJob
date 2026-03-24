using FluentAssertions;
using NexJob;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

public sealed class InMemoryStorageProviderTests
{
    private readonly InMemoryStorageProvider _sut = new();

    // ─── EnqueueAsync ─────────────────────────────────────────────────────────

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
        var job2 = MakeJob(idempotencyKey: "order-42"); // different Id, same key

        var id1 = await _sut.EnqueueAsync(job1);
        var id2 = await _sut.EnqueueAsync(job2);

        id2.Should().Be(id1, "a duplicate idempotency key must return the original job id");
    }

    [Fact]
    public async Task EnqueueAsync_WithIdempotencyKey_AllowsNewJobAfterPreviousSucceeded()
    {
        var job1 = MakeJob(idempotencyKey: "key-1");
        await _sut.EnqueueAsync(job1);

        // Drain the queue and acknowledge the job
        var fetched = await _sut.FetchNextAsync(["default"]);
        await _sut.AcknowledgeAsync(fetched!.Id);

        // Same key — now the job is Succeeded, so a new one should be created
        var job2 = MakeJob(idempotencyKey: "key-1");
        var id2 = await _sut.EnqueueAsync(job2);

        id2.Should().Be(job2.Id, "previous job is no longer active so a new one should be accepted");
    }

    // ─── FetchNextAsync ───────────────────────────────────────────────────────

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
        var job = MakeJob();
        await _sut.EnqueueAsync(job);

        var fetched = await _sut.FetchNextAsync(["default"]);

        fetched!.Status.Should().Be(JobStatus.Processing);
    }

    [Fact]
    public async Task FetchNextAsync_IncrementsAttemptCount()
    {
        var job = MakeJob();
        await _sut.EnqueueAsync(job);

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
        var normalJob   = MakeJob(priority: JobPriority.Normal);
        var criticalJob = MakeJob(priority: JobPriority.Critical);
        var lowJob      = MakeJob(priority: JobPriority.Low);
        var highJob     = MakeJob(priority: JobPriority.High);

        await _sut.EnqueueAsync(normalJob);
        await _sut.EnqueueAsync(lowJob);
        await _sut.EnqueueAsync(criticalJob);
        await _sut.EnqueueAsync(highJob);

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
        var job = MakeJob();
        await _sut.EnqueueAsync(job);

        var first  = await _sut.FetchNextAsync(["default"]);
        var second = await _sut.FetchNextAsync(["default"]);

        first.Should().NotBeNull();
        second.Should().BeNull("the job is already Processing");
    }

    // ─── AcknowledgeAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task AcknowledgeAsync_SetsStatusToSucceeded()
    {
        var job = MakeJob();
        await _sut.EnqueueAsync(job);
        var fetched = await _sut.FetchNextAsync(["default"]);

        await _sut.AcknowledgeAsync(fetched!.Id);

        fetched.Status.Should().Be(JobStatus.Succeeded);
        fetched.CompletedAt.Should().NotBeNull();
    }

    // ─── SetFailedAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task SetFailedAsync_WithRetryAt_SchedulesRetry()
    {
        var job = MakeJob();
        await _sut.EnqueueAsync(job);
        var fetched = await _sut.FetchNextAsync(["default"]);

        var retryAt = DateTimeOffset.UtcNow.AddSeconds(60);
        await _sut.SetFailedAsync(fetched!.Id, new Exception("transient"), retryAt);

        fetched.Status.Should().Be(JobStatus.Scheduled);
        fetched.RetryAt.Should().BeCloseTo(retryAt, TimeSpan.FromSeconds(2));
        fetched.LastErrorMessage.Should().Be("transient");
    }

    [Fact]
    public async Task SetFailedAsync_WithNullRetryAt_MovesJobToDeadLetter()
    {
        var job = MakeJob();
        await _sut.EnqueueAsync(job);
        var fetched = await _sut.FetchNextAsync(["default"]);

        await _sut.SetFailedAsync(fetched!.Id, new Exception("fatal"), retryAt: null);

        fetched.Status.Should().Be(JobStatus.Failed);
        fetched.CompletedAt.Should().NotBeNull();
    }

    // ─── UpdateHeartbeatAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateHeartbeatAsync_RefreshesTimestamp()
    {
        var job = MakeJob();
        await _sut.EnqueueAsync(job);
        var fetched = await _sut.FetchNextAsync(["default"]);
        var originalHeartbeat = fetched!.HeartbeatAt;

        await Task.Delay(10); // ensure time advances
        await _sut.UpdateHeartbeatAsync(fetched.Id);

        fetched.HeartbeatAt.Should().BeAfter(originalHeartbeat!.Value);
    }

    // ─── RequeueOrphanedJobsAsync ─────────────────────────────────────────────

    [Fact]
    public async Task RequeueOrphanedJobsAsync_RequeuesJobWithExpiredHeartbeat()
    {
        var job = MakeJob();
        await _sut.EnqueueAsync(job);
        var fetched = await _sut.FetchNextAsync(["default"]);

        // Force heartbeat into the past
        fetched!.HeartbeatAt = DateTimeOffset.UtcNow.AddMinutes(-10);

        await _sut.RequeueOrphanedJobsAsync(TimeSpan.FromMinutes(5));

        fetched.Status.Should().Be(JobStatus.Enqueued);
        fetched.HeartbeatAt.Should().BeNull();
    }

    [Fact]
    public async Task RequeueOrphanedJobsAsync_DoesNotRequeueActiveJob()
    {
        var job = MakeJob();
        await _sut.EnqueueAsync(job);
        var fetched = await _sut.FetchNextAsync(["default"]);

        // Heartbeat is fresh
        await _sut.UpdateHeartbeatAsync(fetched!.Id);

        await _sut.RequeueOrphanedJobsAsync(TimeSpan.FromMinutes(5));

        fetched.Status.Should().Be(JobStatus.Processing);
    }

    // ─── EnqueueContinuationsAsync ────────────────────────────────────────────

    [Fact]
    public async Task EnqueueContinuationsAsync_ActivatesContinuationJob()
    {
        var parent = MakeJob();
        await _sut.EnqueueAsync(parent);

        var continuation = new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(StubJob).AssemblyQualifiedName!,
            InputType = typeof(string).AssemblyQualifiedName!,
            InputJson = "\"cont\"",
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.AwaitingContinuation,
            ParentJobId = parent.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = 10,
        };

        await _sut.EnqueueAsync(continuation);

        var parentFetched = await _sut.FetchNextAsync(["default"]);
        await _sut.AcknowledgeAsync(parentFetched!.Id);
        await _sut.EnqueueContinuationsAsync(parentFetched.Id);

        var contFetched = await _sut.FetchNextAsync(["default"]);
        contFetched.Should().NotBeNull();
        contFetched!.Id.Should().Be(continuation.Id);
    }

    // ─── Recurring jobs ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetDueRecurringJobsAsync_ReturnsDueJobs()
    {
        var recurring = new RecurringJobRecord
        {
            RecurringJobId = "nightly",
            JobType = typeof(StubJob).AssemblyQualifiedName!,
            InputType = typeof(string).AssemblyQualifiedName!,
            InputJson = "\"go\"",
            Cron = "0 * * * *",
            Queue = "default",
            NextExecution = DateTimeOffset.UtcNow.AddMinutes(-1),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await _sut.UpsertRecurringJobAsync(recurring);

        var due = await _sut.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow);

        due.Should().ContainSingle(r => r.RecurringJobId == "nightly");
    }

    [Fact]
    public async Task GetDueRecurringJobsAsync_DoesNotReturnFutureJobs()
    {
        var recurring = new RecurringJobRecord
        {
            RecurringJobId = "future",
            JobType = typeof(StubJob).AssemblyQualifiedName!,
            InputType = typeof(string).AssemblyQualifiedName!,
            InputJson = "\"go\"",
            Cron = "0 * * * *",
            Queue = "default",
            NextExecution = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await _sut.UpsertRecurringJobAsync(recurring);

        var due = await _sut.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow);

        due.Should().NotContain(r => r.RecurringJobId == "future");
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static JobRecord MakeJob(
        JobPriority priority = JobPriority.Normal,
        string? idempotencyKey = null) =>
        new()
        {
            Id = JobId.New(),
            JobType = typeof(StubJob).AssemblyQualifiedName!,
            InputType = typeof(string).AssemblyQualifiedName!,
            InputJson = "\"test\"",
            Queue = "default",
            Priority = priority,
            Status = JobStatus.Enqueued,
            IdempotencyKey = idempotencyKey,
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = 10,
        };
}

/// <summary>Minimal job used as a test fixture.</summary>
public sealed class StubJob : IJob<string>
{
    /// <inheritdoc/>
    public Task ExecuteAsync(string input, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
