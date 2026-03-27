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
        var normalJob = MakeJob(priority: JobPriority.Normal);
        var criticalJob = MakeJob(priority: JobPriority.Critical);
        var lowJob = MakeJob(priority: JobPriority.Low);
        var highJob = MakeJob(priority: JobPriority.High);

        await _sut.EnqueueAsync(normalJob);
        await _sut.EnqueueAsync(lowJob);
        await _sut.EnqueueAsync(criticalJob);
        await _sut.EnqueueAsync(highJob);

        var first = await _sut.FetchNextAsync(["default"]);
        var second = await _sut.FetchNextAsync(["default"]);
        var third = await _sut.FetchNextAsync(["default"]);
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

        var first = await _sut.FetchNextAsync(["default"]);
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

    // ─── UpdateRecurringJobConfigAsync ───────────────────────────────────────

    [Fact]
    public async Task UpdateRecurringJobConfig_SetsCronOverride()
    {
        var recurring = MakeRecurring("job-override");
        await _sut.UpsertRecurringJobAsync(recurring);

        await _sut.UpdateRecurringJobConfigAsync("job-override", "0 6 * * *", enabled: true);

        var all = await _sut.GetRecurringJobsAsync();
        all.Single(r => r.RecurringJobId == "job-override")
           .CronOverride.Should().Be("0 6 * * *");
    }

    [Fact]
    public async Task UpdateRecurringJobConfig_SetsEnabledFalse()
    {
        var recurring = MakeRecurring("job-pause");
        await _sut.UpsertRecurringJobAsync(recurring);

        await _sut.UpdateRecurringJobConfigAsync("job-pause", cronOverride: null, enabled: false);

        var all = await _sut.GetRecurringJobsAsync();
        all.Single(r => r.RecurringJobId == "job-pause")
           .Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateRecurringJobConfig_ClearsCronOverride_WhenNull()
    {
        var recurring = MakeRecurring("job-clear");
        await _sut.UpsertRecurringJobAsync(recurring);
        await _sut.UpdateRecurringJobConfigAsync("job-clear", "0 6 * * *", enabled: true);

        await _sut.UpdateRecurringJobConfigAsync("job-clear", cronOverride: null, enabled: true);

        var all = await _sut.GetRecurringJobsAsync();
        all.Single(r => r.RecurringJobId == "job-clear")
           .CronOverride.Should().BeNull();
    }

    // ─── ForceDeleteRecurringJobAsync ─────────────────────────────────────────

    [Fact]
    public async Task ForceDelete_SetsDeletedByUserTrue()
    {
        var recurring = MakeRecurring("job-softdelete");
        await _sut.UpsertRecurringJobAsync(recurring);

        await _sut.ForceDeleteRecurringJobAsync("job-softdelete");

        var all = await _sut.GetRecurringJobsAsync();
        var record = all.Single(r => r.RecurringJobId == "job-softdelete");
        record.DeletedByUser.Should().BeTrue();
        record.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task ForceDelete_RemovesAssociatedJobRecords()
    {
        var recurring = MakeRecurring("job-cleanup");
        await _sut.UpsertRecurringJobAsync(recurring);

        // Enqueue two jobs linked to this recurring job
        var job1 = MakeJobForRecurring("job-cleanup");
        var job2 = MakeJobForRecurring("job-cleanup");
        await _sut.EnqueueAsync(job1);
        await _sut.EnqueueAsync(job2);

        await _sut.ForceDeleteRecurringJobAsync("job-cleanup");

        // Neither job should be fetchable
        var result = await _sut.GetJobsAsync(new JobFilter(), page: 1, pageSize: 100);
        result.Items.Should().NotContain(j => j.RecurringJobId == "job-cleanup",
            "associated job records must be removed by ForceDeleteRecurringJobAsync");
    }

    [Fact]
    public async Task ForceDelete_JobStillInList()
    {
        var recurring = MakeRecurring("job-still-listed");
        await _sut.UpsertRecurringJobAsync(recurring);

        await _sut.ForceDeleteRecurringJobAsync("job-still-listed");

        var all = await _sut.GetRecurringJobsAsync();
        all.Should().Contain(r => r.RecurringJobId == "job-still-listed",
            "soft-deleted recurring job must remain in the list");
    }

    // ─── RestoreRecurringJobAsync ─────────────────────────────────────────────

    [Fact]
    public async Task Restore_ClearsDeletedByUserAndEnables()
    {
        var recurring = MakeRecurring("job-restore");
        await _sut.UpsertRecurringJobAsync(recurring);
        await _sut.ForceDeleteRecurringJobAsync("job-restore");

        await _sut.RestoreRecurringJobAsync("job-restore");

        var all = await _sut.GetRecurringJobsAsync();
        var record = all.Single(r => r.RecurringJobId == "job-restore");
        record.DeletedByUser.Should().BeFalse();
        record.Enabled.Should().BeTrue();
    }

    // ─── UpsertRecurringJobAsync preserves user config ────────────────────────

    [Fact]
    public async Task Upsert_PreservesDeletedByUser_OnUpdate()
    {
        var recurring = MakeRecurring("job-preserve-deleted");
        await _sut.UpsertRecurringJobAsync(recurring);
        await _sut.ForceDeleteRecurringJobAsync("job-preserve-deleted");

        // Simulate app restart: upsert the same definition again
        var updated = MakeRecurring("job-preserve-deleted");
        await _sut.UpsertRecurringJobAsync(updated);

        var all = await _sut.GetRecurringJobsAsync();
        all.Single(r => r.RecurringJobId == "job-preserve-deleted")
           .DeletedByUser.Should().BeTrue("upsert must not resurrect a user-deleted job");
    }

    [Fact]
    public async Task Upsert_PreservesCronOverride_OnUpdate()
    {
        var recurring = MakeRecurring("job-preserve-cron");
        await _sut.UpsertRecurringJobAsync(recurring);
        await _sut.UpdateRecurringJobConfigAsync("job-preserve-cron", "0 6 * * *", enabled: true);

        // Simulate app restart with a different default cron — override must be kept
        var updated = MakeRecurring("job-preserve-cron", cron: "0 12 * * *");
        await _sut.UpsertRecurringJobAsync(updated);

        var all = await _sut.GetRecurringJobsAsync();
        all.Single(r => r.RecurringJobId == "job-preserve-cron")
           .CronOverride.Should().Be("0 6 * * *", "upsert must preserve user-set CronOverride");
    }

    [Fact]
    public async Task Upsert_PreservesEnabled_OnUpdate()
    {
        var recurring = MakeRecurring("job-preserve-enabled");
        await _sut.UpsertRecurringJobAsync(recurring);
        await _sut.UpdateRecurringJobConfigAsync("job-preserve-enabled", cronOverride: null, enabled: false);

        // Simulate app restart — disabled state must be kept
        var updated = MakeRecurring("job-preserve-enabled");
        await _sut.UpsertRecurringJobAsync(updated);

        var all = await _sut.GetRecurringJobsAsync();
        all.Single(r => r.RecurringJobId == "job-preserve-enabled")
           .Enabled.Should().BeFalse("upsert must preserve user-set Enabled=false");
    }

    // ─── ReportProgressAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task ReportProgressAsync_UpdatesPercentAndMessage()
    {
        var job = MakeJob();
        await _sut.EnqueueAsync(job);
        var fetched = (await _sut.FetchNextAsync(["default"]))!;

        await _sut.ReportProgressAsync(fetched.Id, 42, "processing items");

        var updated = await _sut.GetJobByIdAsync(fetched.Id);
        updated!.ProgressPercent.Should().Be(42);
        updated.ProgressMessage.Should().Be("processing items");
    }

    [Fact]
    public async Task ReportProgressAsync_NullMessage_ClearsMessage()
    {
        var job = MakeJob();
        await _sut.EnqueueAsync(job);
        var fetched = (await _sut.FetchNextAsync(["default"]))!;
        await _sut.ReportProgressAsync(fetched.Id, 50, "initial");

        await _sut.ReportProgressAsync(fetched.Id, 75, null);

        var updated = await _sut.GetJobByIdAsync(fetched.Id);
        updated!.ProgressPercent.Should().Be(75);
        updated.ProgressMessage.Should().BeNull();
    }

    [Fact]
    public async Task ReportProgressAsync_100Percent_StoresCorrectly()
    {
        var job = MakeJob();
        await _sut.EnqueueAsync(job);
        var fetched = (await _sut.FetchNextAsync(["default"]))!;

        await _sut.ReportProgressAsync(fetched.Id, 100, "done");

        var updated = await _sut.GetJobByIdAsync(fetched.Id);
        updated!.ProgressPercent.Should().Be(100);
    }

    [Fact]
    public async Task ReportProgressAsync_UnknownJob_DoesNotThrow()
    {
        var act = async () => await _sut.ReportProgressAsync(new JobId(Guid.NewGuid()), 50, "msg");

        await act.Should().NotThrowAsync();
    }

    // ─── Tags / GetJobsByTagAsync ─────────────────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_WithTags_PersistsTags()
    {
        var job = MakeJobWithTags(["env:prod", "team:backend"]);

        await _sut.EnqueueAsync(job);

        var stored = await _sut.GetJobByIdAsync(job.Id);
        stored!.Tags.Should().BeEquivalentTo("env:prod", "team:backend");
    }

    [Fact]
    public async Task GetJobsByTagAsync_ReturnsMatchingJobs()
    {
        await _sut.EnqueueAsync(MakeJobWithTags(["env:prod"]));
        await _sut.EnqueueAsync(MakeJobWithTags(["env:staging"]));
        await _sut.EnqueueAsync(MakeJobWithTags(["env:prod", "region:us"]));

        var results = await _sut.GetJobsByTagAsync("env:prod");

        results.Should().HaveCount(2);
        results.Should().AllSatisfy(j => j.Tags.Should().Contain("env:prod"));
    }

    [Fact]
    public async Task GetJobsByTagAsync_NoMatch_ReturnsEmpty()
    {
        await _sut.EnqueueAsync(MakeJobWithTags(["env:prod"]));

        var results = await _sut.GetJobsByTagAsync("env:nonexistent");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetJobsByTagAsync_ExactMatch_NotPartialMatch()
    {
        await _sut.EnqueueAsync(MakeJobWithTags(["env:production"]));

        var results = await _sut.GetJobsByTagAsync("env:prod");

        results.Should().BeEmpty("tag matching must be exact, not prefix-based");
    }

    [Fact]
    public async Task GetJobsByTagAsync_NoTags_ReturnsEmpty()
    {
        await _sut.EnqueueAsync(MakeJob()); // no tags

        var results = await _sut.GetJobsByTagAsync("env:prod");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetJobsByTagAsync_ViaScheduler_RoundTrip()
    {
        var job = MakeJobWithTags(["tenant:acme"]);
        await _sut.EnqueueAsync(job);

        var byTag = await _sut.GetJobsByTagAsync("tenant:acme");

        byTag.Should().ContainSingle(j => j.Id == job.Id);
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

    private static RecurringJobRecord MakeRecurring(string id, string cron = "0 * * * *") =>
        new()
        {
            RecurringJobId = id,
            JobType = typeof(StubJob).AssemblyQualifiedName!,
            InputType = typeof(string).AssemblyQualifiedName!,
            InputJson = "\"go\"",
            Cron = cron,
            Queue = "default",
            NextExecution = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static JobRecord MakeJobForRecurring(string recurringJobId) =>
        new()
        {
            Id = JobId.New(),
            JobType = typeof(StubJob).AssemblyQualifiedName!,
            InputType = typeof(string).AssemblyQualifiedName!,
            InputJson = "\"test\"",
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.Enqueued,
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = 10,
            RecurringJobId = recurringJobId,
        };

    private static JobRecord MakeJobWithTags(IReadOnlyList<string> tags) =>
        new()
        {
            Id = JobId.New(),
            JobType = typeof(StubJob).AssemblyQualifiedName!,
            InputType = typeof(string).AssemblyQualifiedName!,
            InputJson = "\"test\"",
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.Enqueued,
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = 10,
            Tags = tags,
        };
}

/// <summary>Minimal job used as a test fixture.</summary>
public sealed class StubJob : IJob<string>
{
    /// <inheritdoc/>
    public Task ExecuteAsync(string input, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
