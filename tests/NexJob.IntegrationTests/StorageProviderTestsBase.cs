using FluentAssertions;
using NexJob.Storage;
using Xunit;

namespace NexJob.IntegrationTests;

/// <summary>
/// Abstract base class that defines the full contract test suite for any
/// <see cref="IStorageProvider"/> implementation.
/// Subclasses provide the concrete provider via <see cref="CreateStorageAsync"/>.
/// </summary>
public abstract class StorageProviderTestsBase
{
    /// <summary>Creates and returns a ready-to-use storage provider for a clean test run.</summary>
    protected abstract Task<IStorageProvider> CreateStorageAsync();

    // ── Enqueue & fetch ────────────────────────────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_then_FetchNextAsync_returns_the_job()
    {
        var storage = await CreateStorageAsync();
        var record = MakeJob();

        await storage.EnqueueAsync(record);
        var fetched = await storage.FetchNextAsync(["default"]);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(record.Id);
        fetched.Status.Should().Be(JobStatus.Processing);
    }

    [Fact]
    public async Task FetchNextAsync_respects_queue_filter()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob(queue: "high"));
        await storage.EnqueueAsync(MakeJob(queue: "low"));

        var fetched = await storage.FetchNextAsync(["low"]);

        fetched.Should().NotBeNull();
        fetched!.Queue.Should().Be("low");
    }

    [Fact]
    public async Task FetchNextAsync_returns_null_when_queue_is_empty()
    {
        var storage = await CreateStorageAsync();

        var fetched = await storage.FetchNextAsync(["default"]);

        fetched.Should().BeNull();
    }

    [Fact]
    public async Task FetchNextAsync_does_not_return_same_job_twice()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());

        var first = await storage.FetchNextAsync(["default"]);
        var second = await storage.FetchNextAsync(["default"]);

        first.Should().NotBeNull();
        second.Should().BeNull();
    }

    [Fact]
    public async Task FetchNextAsync_returns_higher_priority_job_first()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob(priority: JobPriority.Low));
        await storage.EnqueueAsync(MakeJob(priority: JobPriority.Critical));
        await storage.EnqueueAsync(MakeJob(priority: JobPriority.Normal));

        var first = await storage.FetchNextAsync(["default"]);

        first!.Priority.Should().Be(JobPriority.Critical);
    }

    [Fact]
    public async Task FetchNextAsync_increments_attempts_on_claim()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());

        var fetched = await storage.FetchNextAsync(["default"]);

        fetched!.Attempts.Should().Be(1);
    }

    // ── Status transitions ─────────────────────────────────────────────────────

    [Fact]
    public async Task AcknowledgeAsync_sets_status_to_Succeeded()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;

        await storage.AcknowledgeAsync(fetched.Id);
        var updated = await storage.GetJobByIdAsync(fetched.Id);

        updated!.Status.Should().Be(JobStatus.Succeeded);
        updated.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SetFailedAsync_with_retryAt_re_enqueues_job()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;

        var retryAt = DateTimeOffset.UtcNow.AddMinutes(1);
        await storage.SetFailedAsync(fetched.Id,
            new InvalidOperationException("boom"), retryAt);

        var updated = await storage.GetJobByIdAsync(fetched.Id);
        updated!.LastErrorMessage.Should().Contain("boom");
        updated.RetryAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SetFailedAsync_without_retryAt_moves_job_to_Failed()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;

        await storage.SetFailedAsync(fetched.Id,
            new InvalidOperationException("fatal"), retryAt: null);

        var updated = await storage.GetJobByIdAsync(fetched.Id);
        updated!.Status.Should().Be(JobStatus.Failed);
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_updates_heartbeat_timestamp()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;
        var before = fetched.HeartbeatAt;

        await Task.Delay(10); // ensure clock advances
        await storage.UpdateHeartbeatAsync(fetched.Id);
        var updated = await storage.GetJobByIdAsync(fetched.Id);

        updated!.HeartbeatAt.Should().NotBeNull();
        updated.HeartbeatAt.Should().BeAfter(before ?? DateTimeOffset.MinValue);
    }

    // ── Idempotency ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_with_same_idempotency_key_is_deduplicated()
    {
        var storage = await CreateStorageAsync();
        var key = Guid.NewGuid().ToString();

        var id1 = await storage.EnqueueAsync(MakeJob(idempotencyKey: key));
        var id2 = await storage.EnqueueAsync(MakeJob(idempotencyKey: key));

        id1.Should().Be(id2, "second enqueue with same key must return the existing job id");
        var metrics = await storage.GetMetricsAsync();
        metrics.Enqueued.Should().Be(1);
    }

    // ── Orphan requeue ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RequeueOrphanedJobsAsync_requeues_stale_processing_job()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;
        fetched.Status.Should().Be(JobStatus.Processing);

        // heartbeat_at was just set; pass zero timeout so the cutoff = UtcNow,
        // which is after the heartbeat that was set a few ms ago
        await Task.Delay(10);
        await storage.RequeueOrphanedJobsAsync(TimeSpan.Zero);

        var updated = await storage.GetJobByIdAsync(fetched.Id);
        updated!.Status.Should().Be(JobStatus.Enqueued);
        updated.HeartbeatAt.Should().BeNull();
    }

    [Fact]
    public async Task RequeueOrphanedJobsAsync_does_not_touch_fresh_heartbeat()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;

        // Large timeout — the job heartbeat is fresh, should not be requeued
        await storage.RequeueOrphanedJobsAsync(TimeSpan.FromMinutes(5));

        var updated = await storage.GetJobByIdAsync(fetched.Id);
        updated!.Status.Should().Be(JobStatus.Processing);
    }

    // ── Continuations ──────────────────────────────────────────────────────────

    [Fact]
    public async Task EnqueueContinuationsAsync_releases_awaiting_jobs()
    {
        var storage = await CreateStorageAsync();
        var parentId = new JobId(Guid.NewGuid());
        var child = MakeJob(status: JobStatus.AwaitingContinuation, parentJobId: parentId);
        await storage.EnqueueAsync(child);

        await storage.EnqueueContinuationsAsync(parentId);

        var updated = await storage.GetJobByIdAsync(child.Id);
        updated!.Status.Should().Be(JobStatus.Enqueued);
    }

    [Fact]
    public async Task EnqueueContinuationsAsync_does_not_release_other_parents_jobs()
    {
        var storage = await CreateStorageAsync();
        var parentA = new JobId(Guid.NewGuid());
        var parentB = new JobId(Guid.NewGuid());
        var childOfB = MakeJob(status: JobStatus.AwaitingContinuation, parentJobId: parentB);
        await storage.EnqueueAsync(childOfB);

        await storage.EnqueueContinuationsAsync(parentA);

        var updated = await storage.GetJobByIdAsync(childOfB.Id);
        updated!.Status.Should().Be(JobStatus.AwaitingContinuation, "only parentB's children should be released");
    }

    // ── Dashboard methods ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetMetricsAsync_counts_enqueued_jobs()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        await storage.EnqueueAsync(MakeJob());

        var metrics = await storage.GetMetricsAsync();

        metrics.Enqueued.Should().Be(2);
    }

    [Fact]
    public async Task GetMetricsAsync_counts_all_statuses()
    {
        var storage = await CreateStorageAsync();

        // → Processing: fetch job A, leave it running
        await storage.EnqueueAsync(MakeJob());
        await storage.FetchNextAsync(["default"]); // leave as Processing

        // → Succeeded: fetch job B, acknowledge it
        await storage.EnqueueAsync(MakeJob());
        var toSucceed = (await storage.FetchNextAsync(["default"]))!;
        await storage.AcknowledgeAsync(toSucceed.Id);

        // → Failed: fetch job C, fail it with no retry
        await storage.EnqueueAsync(MakeJob());
        var toFail = (await storage.FetchNextAsync(["default"]))!;
        await storage.SetFailedAsync(toFail.Id, new Exception("x"), retryAt: null);

        // → Enqueued: job D — just enqueued, not fetched
        await storage.EnqueueAsync(MakeJob());

        // → Recurring
        await storage.UpsertRecurringJobAsync(MakeRecurring());

        var metrics = await storage.GetMetricsAsync();

        metrics.Enqueued.Should().Be(1);
        metrics.Processing.Should().Be(1);
        metrics.Succeeded.Should().Be(1);
        metrics.Failed.Should().Be(1);
        metrics.Recurring.Should().Be(1);
    }

    [Fact]
    public async Task GetJobsAsync_returns_paged_results()
    {
        var storage = await CreateStorageAsync();
        for (var i = 0; i < 5; i++)
        {
            await storage.EnqueueAsync(MakeJob());
        }

        var page = await storage.GetJobsAsync(new JobFilter(), page: 1, pageSize: 3);

        page.Items.Should().HaveCount(3);
        page.TotalCount.Should().Be(5);
        page.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task GetJobsAsync_filters_by_status()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;
        await storage.AcknowledgeAsync(fetched.Id);
        await storage.EnqueueAsync(MakeJob()); // still Enqueued

        var page = await storage.GetJobsAsync(
            new JobFilter { Status = JobStatus.Succeeded }, page: 1, pageSize: 10);

        page.Items.Should().HaveCount(1);
        page.Items[0].Status.Should().Be(JobStatus.Succeeded);
    }

    [Fact]
    public async Task GetJobsAsync_filters_by_queue()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob(queue: "alpha"));
        await storage.EnqueueAsync(MakeJob(queue: "alpha"));
        await storage.EnqueueAsync(MakeJob(queue: "beta"));

        var page = await storage.GetJobsAsync(
            new JobFilter { Queue = "alpha" }, page: 1, pageSize: 10);

        page.TotalCount.Should().Be(2);
        page.Items.Should().AllSatisfy(j => j.Queue.Should().Be("alpha"));
    }

    [Fact]
    public async Task GetJobByIdAsync_returns_null_for_unknown_id()
    {
        var storage = await CreateStorageAsync();

        var result = await storage.GetJobByIdAsync(new JobId(Guid.NewGuid()));

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteJobAsync_removes_the_job()
    {
        var storage = await CreateStorageAsync();
        var job = MakeJob();
        await storage.EnqueueAsync(job);

        await storage.DeleteJobAsync(job.Id);
        var result = await storage.GetJobByIdAsync(job.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RequeueJobAsync_resets_failed_job_to_enqueued()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;
        await storage.SetFailedAsync(fetched.Id,
            new Exception("error"), retryAt: null);

        await storage.RequeueJobAsync(fetched.Id);
        var updated = await storage.GetJobByIdAsync(fetched.Id);

        updated!.Status.Should().Be(JobStatus.Enqueued);
        updated.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task GetQueueMetricsAsync_returns_counts_per_queue()
    {
        var storage = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob(queue: "alpha"));
        await storage.EnqueueAsync(MakeJob(queue: "alpha"));
        await storage.EnqueueAsync(MakeJob(queue: "beta"));
        // Claim one from alpha → Processing
        await storage.FetchNextAsync(["alpha"]);

        var metrics = await storage.GetQueueMetricsAsync();

        var alpha = metrics.First(q => q.Queue == "alpha");
        var beta = metrics.First(q => q.Queue == "beta");

        alpha.Enqueued.Should().Be(1);
        alpha.Processing.Should().Be(1);
        beta.Enqueued.Should().Be(1);
        beta.Processing.Should().Be(0);
    }

    // ── Recurring jobs ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertRecurringJobAsync_and_GetRecurringJobsAsync_roundtrip()
    {
        var storage = await CreateStorageAsync();
        var recurring = MakeRecurring();

        await storage.UpsertRecurringJobAsync(recurring);
        var all = await storage.GetRecurringJobsAsync();

        var saved = all.Should().ContainSingle(r => r.RecurringJobId == recurring.RecurringJobId).Subject;
        saved.Cron.Should().Be(recurring.Cron);
        saved.Queue.Should().Be(recurring.Queue);
        saved.ConcurrencyPolicy.Should().Be(recurring.ConcurrencyPolicy);
    }

    [Fact]
    public async Task UpsertRecurringJobAsync_updates_existing_definition()
    {
        var storage = await CreateStorageAsync();
        var id = $"upsert-test-{Guid.NewGuid()}";
        await storage.UpsertRecurringJobAsync(MakeRecurring(id: id, cron: "* * * * *"));

        await storage.UpsertRecurringJobAsync(MakeRecurring(id: id, cron: "0 9 * * *"));
        var all = await storage.GetRecurringJobsAsync();

        all.Should().ContainSingle(r => r.RecurringJobId == id)
           .Which.Cron.Should().Be("0 9 * * *");
    }

    [Fact]
    public async Task GetDueRecurringJobsAsync_returns_only_due_jobs()
    {
        var storage = await CreateStorageAsync();
        var dueId = $"due-{Guid.NewGuid()}";
        var futureId = $"future-{Guid.NewGuid()}";

        await storage.UpsertRecurringJobAsync(
            MakeRecurring(id: dueId, nextExecution: DateTimeOffset.UtcNow.AddSeconds(-1)));
        await storage.UpsertRecurringJobAsync(
            MakeRecurring(id: futureId, nextExecution: DateTimeOffset.UtcNow.AddHours(1)));

        var due = await storage.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow);

        due.Should().Contain(r => r.RecurringJobId == dueId);
        due.Should().NotContain(r => r.RecurringJobId == futureId);
    }

    [Fact]
    public async Task SetRecurringJobNextExecutionAsync_persists_next_and_last_execution()
    {
        var storage = await CreateStorageAsync();
        var recurring = MakeRecurring();
        await storage.UpsertRecurringJobAsync(recurring);

        var next = DateTimeOffset.UtcNow.AddMinutes(5);
        await storage.SetRecurringJobNextExecutionAsync(recurring.RecurringJobId, next);

        var all = await storage.GetRecurringJobsAsync();
        var saved = all.Single(r => r.RecurringJobId == recurring.RecurringJobId);

        saved.NextExecution.Should().NotBeNull();
        saved.LastExecutedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SetRecurringJobLastExecutionResultAsync_records_success()
    {
        var storage = await CreateStorageAsync();
        var recurring = MakeRecurring();
        await storage.UpsertRecurringJobAsync(recurring);

        await storage.SetRecurringJobLastExecutionResultAsync(
            recurring.RecurringJobId, JobStatus.Succeeded, errorMessage: null);

        var all = await storage.GetRecurringJobsAsync();
        var saved = all.Single(r => r.RecurringJobId == recurring.RecurringJobId);

        saved.LastExecutionStatus.Should().Be(JobStatus.Succeeded);
        saved.LastExecutionError.Should().BeNull();
    }

    [Fact]
    public async Task SetRecurringJobLastExecutionResultAsync_records_failure_with_error()
    {
        var storage = await CreateStorageAsync();
        var recurring = MakeRecurring();
        await storage.UpsertRecurringJobAsync(recurring);

        await storage.SetRecurringJobLastExecutionResultAsync(
            recurring.RecurringJobId, JobStatus.Failed, errorMessage: "timeout");

        var all = await storage.GetRecurringJobsAsync();
        var saved = all.Single(r => r.RecurringJobId == recurring.RecurringJobId);

        saved.LastExecutionStatus.Should().Be(JobStatus.Failed);
        saved.LastExecutionError.Should().Be("timeout");
    }

    [Fact]
    public async Task DeleteRecurringJobAsync_removes_the_definition()
    {
        var storage = await CreateStorageAsync();
        var recurring = MakeRecurring();
        await storage.UpsertRecurringJobAsync(recurring);

        await storage.DeleteRecurringJobAsync(recurring.RecurringJobId);
        var all = await storage.GetRecurringJobsAsync();

        all.Should().NotContain(r => r.RecurringJobId == recurring.RecurringJobId);
    }

    // ── Server Tracking ────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterServerAsync_AddsServer_WhenSupported()
    {
        var storage = await CreateStorageAsync();
        if (IsServerTrackingNotSupported(storage))
        {
            return;
        }

        var serverId = $"server-add-{Guid.NewGuid()}";
        var server = MakeServer(serverId);

        await storage.RegisterServerAsync(server);

        var active = await storage.GetActiveServersAsync(TimeSpan.FromMinutes(1));
        active.Should().Contain(s => s.Id == serverId);
    }

    [Fact]
    public async Task HeartbeatServerAsync_UpdatesTimestamp_WhenSupported()
    {
        var storage = await CreateStorageAsync();
        if (IsServerTrackingNotSupported(storage))
        {
            return;
        }

        var serverId = $"server-hb-{Guid.NewGuid()}";
        var server = MakeServer(serverId, DateTimeOffset.UtcNow.AddMinutes(-5));

        await storage.RegisterServerAsync(server);
        await Task.Delay(100);

        await storage.HeartbeatServerAsync(serverId);

        var active = await storage.GetActiveServersAsync(TimeSpan.FromMinutes(1));
        var updated = active.Single(s => s.Id == serverId);
        updated.HeartbeatAt.Should().BeOnOrAfter(server.HeartbeatAt);
    }

    [Fact]
    public async Task DeregisterServerAsync_RemovesServer_WhenSupported()
    {
        var storage = await CreateStorageAsync();
        if (IsServerTrackingNotSupported(storage))
        {
            return;
        }

        var serverId = $"server-dereg-{Guid.NewGuid()}";
        await storage.RegisterServerAsync(MakeServer(serverId));

        await storage.DeregisterServerAsync(serverId);

        var active = await storage.GetActiveServersAsync(TimeSpan.FromMinutes(1));
        active.Should().NotContain(s => s.Id == serverId);
    }

    [Fact]
    public async Task GetActiveServersAsync_FiltersStaleServers_WhenSupported()
    {
        var storage = await CreateStorageAsync();
        if (IsServerTrackingNotSupported(storage))
        {
            return;
        }

        var activeId = $"active-{Guid.NewGuid()}";
        var staleId = $"stale-{Guid.NewGuid()}";

        await storage.RegisterServerAsync(MakeServer(activeId, DateTimeOffset.UtcNow));
        await storage.RegisterServerAsync(MakeServer(staleId, DateTimeOffset.UtcNow.AddMinutes(-10)));

        var active = await storage.GetActiveServersAsync(TimeSpan.FromMinutes(1));

        active.Should().Contain(s => s.Id == activeId);
        active.Should().NotContain(s => s.Id == staleId);
    }

    // ── CommitJobResultAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task CommitJobResultAsync_Success_WithContinuation_EnqueuesContinuation()
    {
        var storage = await CreateStorageAsync();

        // Create and fetch parent job
        var parent = MakeJob();
        await storage.EnqueueAsync(parent);
        var parentFetched = (await storage.FetchNextAsync(["default"]))!;

        // Create child awaiting parent continuation
        var child = MakeJob(status: JobStatus.AwaitingContinuation, parentJobId: parentFetched.Id);
        await storage.EnqueueAsync(child);

        // Commit parent as succeeded
        var logs = new[] { new JobExecutionLog { Timestamp = DateTimeOffset.UtcNow, Level = "Information", Message = "Success" } };
        await storage.CommitJobResultAsync(parentFetched.Id, new JobExecutionResult
        {
            Succeeded = true,
            Logs = logs,
            RecurringJobId = null,
        });

        // Verify parent is succeeded and child is now enqueued
        var parentResult = await storage.GetJobByIdAsync(parentFetched.Id);
        parentResult!.Status.Should().Be(JobStatus.Succeeded);
        parentResult.ExecutionLogs.Should().HaveCount(1);

        var childResult = await storage.GetJobByIdAsync(child.Id);
        childResult!.Status.Should().Be(JobStatus.Enqueued);
    }

    [Fact]
    public async Task CommitJobResultAsync_Failure_WithRetry_SetsScheduled()
    {
        var storage = await CreateStorageAsync();

        var job = MakeJob();
        await storage.EnqueueAsync(job);
        var fetched = (await storage.FetchNextAsync(["default"]))!;

        var ex = new InvalidOperationException("test failure");
        var retryAt = DateTimeOffset.UtcNow.AddMinutes(5);
        var logs = new[] { new JobExecutionLog { Timestamp = DateTimeOffset.UtcNow, Level = "Error", Message = "Failure" } };

        await storage.CommitJobResultAsync(fetched.Id, new JobExecutionResult
        {
            Succeeded = false,
            Logs = logs,
            Exception = ex,
            RetryAt = retryAt,
            RecurringJobId = null,
        });

        var result = await storage.GetJobByIdAsync(fetched.Id);
        result!.Status.Should().Be(JobStatus.Scheduled);
        result.RetryAt.Should().BeCloseTo(retryAt, TimeSpan.FromMilliseconds(1));
        result.LastErrorMessage.Should().Contain("test failure");
        result.ExecutionLogs.Should().HaveCount(1);
    }

    [Fact]
    public async Task CommitJobResultAsync_Failure_NoRetry_SetsFailed()
    {
        var storage = await CreateStorageAsync();

        var job = MakeJob();
        await storage.EnqueueAsync(job);
        var fetched = (await storage.FetchNextAsync(["default"]))!;

        var ex = new InvalidOperationException("permanent failure");
        var logs = new[] { new JobExecutionLog { Timestamp = DateTimeOffset.UtcNow, Level = "Error", Message = "Dead letter" } };

        await storage.CommitJobResultAsync(fetched.Id, new JobExecutionResult
        {
            Succeeded = false,
            Logs = logs,
            Exception = ex,
            RetryAt = null,
            RecurringJobId = null,
        });

        var result = await storage.GetJobByIdAsync(fetched.Id);
        result!.Status.Should().Be(JobStatus.Failed);
        result.CompletedAt.Should().NotBeNull();
        result.LastErrorMessage.Should().Contain("permanent failure");
    }

    [Fact]
    public async Task CommitJobResultAsync_IsIdempotent_WhenCalledTwice()
    {
        var storage = await CreateStorageAsync();

        var job = MakeJob();
        await storage.EnqueueAsync(job);
        var fetched = (await storage.FetchNextAsync(["default"]))!;

        var logs1 = new[] { new JobExecutionLog { Timestamp = DateTimeOffset.UtcNow, Level = "Information", Message = "First call" } };

        var result = new JobExecutionResult
        {
            Succeeded = true,
            Logs = logs1,
            RecurringJobId = null,
        };

        // Call twice with same job id
        await storage.CommitJobResultAsync(fetched.Id, result);
        await storage.CommitJobResultAsync(fetched.Id, result); // Should be no-op

        var final = await storage.GetJobByIdAsync(fetched.Id);
        final!.Status.Should().Be(JobStatus.Succeeded);
        // Logs should be from first call only (second commit was no-op)
        final.ExecutionLogs.Should().HaveCount(1);
        final.ExecutionLogs[0].Message.Should().Be("First call");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static JobRecord MakeJob(
        string queue = "default",
        string? idempotencyKey = null,
        JobPriority priority = JobPriority.Normal,
        JobStatus status = JobStatus.Enqueued,
        JobId? parentJobId = null) =>
        new()
        {
            Id = new JobId(Guid.NewGuid()),
            JobType = "NexJob.IntegrationTests.FakeJob",
            InputType = "NexJob.IntegrationTests.FakeInput",
            InputJson = $"{{\"seq\":\"{Guid.NewGuid()}\"}}",
            Queue = queue,
            Priority = priority,
            Status = status,
            MaxAttempts = 5,
            CreatedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = idempotencyKey,
            ParentJobId = parentJobId,
        };

    private static RecurringJobRecord MakeRecurring(
        string? id = null,
        string cron = "* * * * *",
        DateTimeOffset? nextExecution = null) =>
        new()
        {
            RecurringJobId = id ?? $"test-recurring-{Guid.NewGuid()}",
            JobType = "NexJob.IntegrationTests.FakeJob",
            InputType = "NexJob.IntegrationTests.FakeInput",
            InputJson = "{}",
            Cron = cron,
            Queue = "default",
            NextExecution = nextExecution ?? DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            ConcurrencyPolicy = RecurringConcurrencyPolicy.SkipIfRunning,
        };

    private static ServerRecord MakeServer(string id, DateTimeOffset? heartbeatAt = null) =>
        new()
        {
            Id = id,
            WorkerCount = 10,
            Queues = ["default", "critical"],
            StartedAt = DateTimeOffset.UtcNow,
            HeartbeatAt = heartbeatAt ?? DateTimeOffset.UtcNow,
        };

    private static bool IsServerTrackingNotSupported(IStorageProvider storage)
    {
        var name = storage.GetType().Name;
        return name.Contains("Redis") || name.Contains("SqlServer");
    }

    [Fact]
    public async Task PurgeJobsAsync_DeletesTerminalJobsBeyondRetention()
    {
        var storage = await CreateStorageAsync();

        // Use RetainSucceeded = 1 second to make test deterministic
        var policy = new RetentionPolicy
        {
            RetainSucceeded = TimeSpan.FromSeconds(1),
            RetainFailed = TimeSpan.Zero,
            RetainExpired = TimeSpan.Zero,
        };

        var job = MakeJob();
        await storage.EnqueueAsync(job);
        var fetched = await storage.FetchNextAsync(["default"]);
        await storage.CommitJobResultAsync(fetched!.Id, new JobExecutionResult
        {
            Succeeded = true,
            Logs = [],
        });

        // Wait for threshold to pass
        await Task.Delay(TimeSpan.FromSeconds(2));

        var deleted = await storage.PurgeJobsAsync(policy);

        deleted.Should().Be(1);
        var remaining = await storage.GetJobByIdAsync(job.Id);
        remaining.Should().BeNull();
    }

    [Fact]
    public async Task PurgeJobsAsync_PreservesJobsWithinRetention()
    {
        var storage = await CreateStorageAsync();

        var policy = new RetentionPolicy
        {
            RetainSucceeded = TimeSpan.FromDays(7), // Long — won't purge
            RetainFailed = TimeSpan.Zero,
            RetainExpired = TimeSpan.Zero,
        };

        var job = MakeJob();
        await storage.EnqueueAsync(job);
        var fetched = await storage.FetchNextAsync(["default"]);
        await storage.CommitJobResultAsync(fetched!.Id, new JobExecutionResult
        {
            Succeeded = true,
            Logs = [],
        });

        var deleted = await storage.PurgeJobsAsync(policy);

        deleted.Should().Be(0);
        var remaining = await storage.GetJobByIdAsync(job.Id);
        remaining.Should().NotBeNull();
    }
}
