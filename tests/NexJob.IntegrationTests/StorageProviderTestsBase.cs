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
    protected abstract Task<(IJobStorage Job, IRecurringStorage Recurring, IDashboardStorage Dashboard, IStorageProvider Full)> CreateStorageAsync();

    [Fact]
    public async Task IJobStorage_resolved_independently_executes_job_lifecycle()
    {
        var (job, _, dashboard, _) = await CreateStorageAsync();
        var record = MakeJob();

        await job.EnqueueAsync(record);
        var fetched = await job.FetchNextAsync(["default"]);

        fetched.Should().NotBeNull();
        await job.CommitJobResultAsync(fetched!.Id, new JobExecutionResult { Succeeded = true, Logs = [] });

        var updated = await dashboard.GetJobByIdAsync(record.Id);
        updated!.Status.Should().Be(JobStatus.Succeeded);
    }

    [Fact]
    public async Task IRecurringStorage_resolved_independently_manages_recurring_lifecycle()
    {
        var (_, recurring, _, _) = await CreateStorageAsync();
        var record = MakeRecurring();

        await recurring.UpsertRecurringJobAsync(record);
        var all = await recurring.GetRecurringJobsAsync();

        all.Should().ContainSingle(r => r.RecurringJobId == record.RecurringJobId);
    }

    [Fact]
    public async Task IDashboardStorage_resolved_independently_queries_metrics()
    {
        var (job, _, dashboard, _) = await CreateStorageAsync();
        await job.EnqueueAsync(MakeJob());

        var metrics = await dashboard.GetMetricsAsync();
        metrics.Enqueued.Should().Be(1);
    }

    // ── Enqueue & fetch ────────────────────────────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_then_FetchNextAsync_returns_the_job()
    {
        var (storage, _, _, _) = await CreateStorageAsync();
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
        var (storage, _, _, _) = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob(queue: "high"));
        await Task.Delay(1);
        await storage.EnqueueAsync(MakeJob(queue: "low"));

        var fetched = await storage.FetchNextAsync(["low"]);

        fetched.Should().NotBeNull();
        fetched!.Queue.Should().Be("low");
    }

    [Fact]
    public async Task FetchNextAsync_returns_null_when_queue_is_empty()
    {
        var (storage, _, _, _) = await CreateStorageAsync();

        var fetched = await storage.FetchNextAsync(["default"]);

        fetched.Should().BeNull();
    }

    [Fact]
    public async Task FetchNextAsync_does_not_return_same_job_twice()
    {
        var (storage, _, _, _) = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());

        var first = await storage.FetchNextAsync(["default"]);
        var second = await storage.FetchNextAsync(["default"]);

        first.Should().NotBeNull();
        second.Should().BeNull();
    }

    [Fact]
    public async Task FetchNextAsync_returns_higher_priority_job_first()
    {
        var (storage, _, _, _) = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob(priority: JobPriority.Low));
        await Task.Delay(1); // ensure distinct CreatedAt for tiebreaker
        await storage.EnqueueAsync(MakeJob(priority: JobPriority.Critical));
        await Task.Delay(1);
        await storage.EnqueueAsync(MakeJob(priority: JobPriority.Normal));

        var first = await storage.FetchNextAsync(["default"]);

        first!.Priority.Should().Be(JobPriority.Critical);
    }

    [Fact]
    public async Task FetchNextAsync_increments_attempts_on_claim()
    {
        var (storage, _, _, _) = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());

        var fetched = await storage.FetchNextAsync(["default"]);

        fetched!.Attempts.Should().Be(1);
    }

    // ── Status transitions ─────────────────────────────────────────────────────

    [Fact]
    public async Task AcknowledgeAsync_sets_status_to_Succeeded()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;

        await storage.AcknowledgeAsync(fetched.Id);
        var updated = await dashboard.GetJobByIdAsync(fetched.Id);

        updated!.Status.Should().Be(JobStatus.Succeeded);
        updated.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SetFailedAsync_with_retryAt_re_enqueues_job()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;

        var retryAt = DateTimeOffset.UtcNow.AddMinutes(1);
        await storage.SetFailedAsync(fetched.Id,
            new InvalidOperationException("boom"), retryAt);

        var updated = await dashboard.GetJobByIdAsync(fetched.Id);
        updated!.LastErrorMessage.Should().Contain("boom");
        updated.RetryAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SetFailedAsync_without_retryAt_moves_job_to_Failed()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;

        await storage.SetFailedAsync(fetched.Id,
            new InvalidOperationException("fatal"), retryAt: null);

        var updated = await dashboard.GetJobByIdAsync(fetched.Id);
        updated!.Status.Should().Be(JobStatus.Failed);
    }

    [Fact]
    public async Task UpdateHeartbeatAsync_updates_heartbeat_timestamp()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;
        var before = fetched.HeartbeatAt;

        await Task.Delay(10); // ensure clock advances
        await storage.UpdateHeartbeatAsync(fetched.Id);
        var updated = await dashboard.GetJobByIdAsync(fetched.Id);

        updated!.HeartbeatAt.Should().NotBeNull();
        updated.HeartbeatAt.Should().BeAfter(before ?? DateTimeOffset.MinValue);
    }

    // ── Idempotency ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_with_same_idempotency_key_is_deduplicated()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();
        var key = Guid.NewGuid().ToString();

        var id1 = await storage.EnqueueAsync(MakeJob(idempotencyKey: key));
        var id2 = await storage.EnqueueAsync(MakeJob(idempotencyKey: key));

        id1.JobId.Should().Be(id2.JobId, "second enqueue with same key must return the existing job id");
        var metrics = await dashboard.GetMetricsAsync();
        metrics.Enqueued.Should().Be(1);
    }

    // ── Orphan requeue ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RequeueOrphanedJobsAsync_requeues_stale_processing_job()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;
        fetched.Status.Should().Be(JobStatus.Processing);

        // heartbeat_at was just set; pass zero timeout so the cutoff = UtcNow,
        // which is after the heartbeat that was set a few ms ago
        await Task.Delay(10);
        await storage.RequeueOrphanedJobsAsync(TimeSpan.Zero);

        var updated = await dashboard.GetJobByIdAsync(fetched.Id);
        updated!.Status.Should().Be(JobStatus.Enqueued);
        updated.HeartbeatAt.Should().BeNull();
    }

    [Fact]
    public async Task RequeueOrphanedJobsAsync_does_not_touch_fresh_heartbeat()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;

        // Large timeout — the job heartbeat is fresh, should not be requeued
        await storage.RequeueOrphanedJobsAsync(TimeSpan.FromMinutes(5));

        var updated = await dashboard.GetJobByIdAsync(fetched.Id);
        updated!.Status.Should().Be(JobStatus.Processing);
    }

    // ── Continuations ──────────────────────────────────────────────────────────

    [Fact]
    public async Task EnqueueContinuationsAsync_releases_awaiting_jobs()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();
        var parentId = new JobId(Guid.NewGuid());
        var child = MakeJob(status: JobStatus.AwaitingContinuation, parentJobId: parentId);
        await storage.EnqueueAsync(child);

        await storage.EnqueueContinuationsAsync(parentId);

        var updated = await dashboard.GetJobByIdAsync(child.Id);
        updated!.Status.Should().Be(JobStatus.Enqueued);
    }

    [Fact]
    public async Task EnqueueContinuationsAsync_does_not_release_other_parents_jobs()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();
        var parentA = new JobId(Guid.NewGuid());
        var parentB = new JobId(Guid.NewGuid());
        var childOfB = MakeJob(status: JobStatus.AwaitingContinuation, parentJobId: parentB);
        await storage.EnqueueAsync(childOfB);

        await storage.EnqueueContinuationsAsync(parentA);

        var updated = await dashboard.GetJobByIdAsync(childOfB.Id);
        updated!.Status.Should().Be(JobStatus.AwaitingContinuation, "only parentB's children should be released");
    }

    // ── Dashboard methods ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetMetricsAsync_counts_enqueued_jobs()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        await Task.Delay(1);
        await storage.EnqueueAsync(MakeJob());

        var metrics = await dashboard.GetMetricsAsync();

        metrics.Enqueued.Should().Be(2);
    }

    [Fact]
    public async Task GetMetricsAsync_counts_all_statuses()
    {
        var (storage, recurring, dashboard, _) = await CreateStorageAsync();

        // → Processing: fetch job A, leave it running
        await storage.EnqueueAsync(MakeJob());
        await Task.Delay(1);
        await storage.FetchNextAsync(["default"]); // leave as Processing

        // → Succeeded: fetch job B, acknowledge it
        await Task.Delay(1);
        await storage.EnqueueAsync(MakeJob());
        var toSucceed = (await storage.FetchNextAsync(["default"]))!;
        await storage.AcknowledgeAsync(toSucceed.Id);

        // → Failed: fetch job C, fail it with no retry
        await Task.Delay(1);
        await storage.EnqueueAsync(MakeJob());
        var toFail = (await storage.FetchNextAsync(["default"]))!;
        await storage.SetFailedAsync(toFail.Id, new Exception("x"), retryAt: null);

        // → Enqueued: job D — just enqueued, not fetched
        await Task.Delay(1);
        await storage.EnqueueAsync(MakeJob());

        // → Recurring
        await recurring.UpsertRecurringJobAsync(MakeRecurring());

        var metrics = await dashboard.GetMetricsAsync();

        metrics.Enqueued.Should().Be(1);
        metrics.Processing.Should().Be(1);
        metrics.Succeeded.Should().Be(1);
        metrics.Failed.Should().Be(1);
        metrics.Recurring.Should().Be(1);
    }

    [Fact]
    public async Task GetJobsAsync_returns_paged_results()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();
        for (var i = 0; i < 5; i++)
        {
            await storage.EnqueueAsync(MakeJob());
            await Task.Delay(1);
        }

        var page = await dashboard.GetJobsAsync(new JobFilter(), page: 1, pageSize: 3);

        page.Items.Should().HaveCount(3);
        page.TotalCount.Should().Be(5);
        page.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task GetJobsAsync_filters_by_status()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;
        await storage.AcknowledgeAsync(fetched.Id);
        await storage.EnqueueAsync(MakeJob()); // still Enqueued

        var page = await dashboard.GetJobsAsync(
            new JobFilter { Status = JobStatus.Succeeded }, page: 1, pageSize: 10);

        page.Items.Should().HaveCount(1);
        page.Items[0].Status.Should().Be(JobStatus.Succeeded);
    }

    [Fact]
    public async Task GetJobsAsync_filters_by_queue()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob(queue: "alpha"));
        await Task.Delay(1);
        await storage.EnqueueAsync(MakeJob(queue: "alpha"));
        await Task.Delay(1);
        await storage.EnqueueAsync(MakeJob(queue: "beta"));

        var page = await dashboard.GetJobsAsync(
            new JobFilter { Queue = "alpha" }, page: 1, pageSize: 10);

        page.TotalCount.Should().Be(2);
        page.Items.Should().AllSatisfy(j => j.Queue.Should().Be("alpha"));
    }

    [Fact]
    public async Task GetJobByIdAsync_returns_null_for_unknown_id()
    {
        var (_, _, dashboard, _) = await CreateStorageAsync();

        var result = await dashboard.GetJobByIdAsync(new JobId(Guid.NewGuid()));

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteJobAsync_removes_the_job()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();
        var job = MakeJob();
        await storage.EnqueueAsync(job);

        await dashboard.DeleteJobAsync(job.Id);
        var result = await dashboard.GetJobByIdAsync(job.Id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task RequeueJobAsync_resets_failed_job_to_enqueued()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob());
        var fetched = (await storage.FetchNextAsync(["default"]))!;
        await storage.SetFailedAsync(fetched.Id,
            new Exception("error"), retryAt: null);

        await dashboard.RequeueJobAsync(fetched.Id);
        var updated = await dashboard.GetJobByIdAsync(fetched.Id);

        updated!.Status.Should().Be(JobStatus.Enqueued);
        updated.Attempts.Should().Be(0);
    }

    [Fact]
    public async Task GetQueueMetricsAsync_returns_counts_per_queue()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();
        await storage.EnqueueAsync(MakeJob(queue: "alpha"));
        await Task.Delay(1);
        await storage.EnqueueAsync(MakeJob(queue: "alpha"));
        await Task.Delay(1);
        await storage.EnqueueAsync(MakeJob(queue: "beta"));
        // Claim one from alpha → Processing
        await Task.Delay(1);
        await storage.FetchNextAsync(["alpha"]);

        var metrics = await dashboard.GetQueueMetricsAsync();

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
        var (_, recurring, _, _) = await CreateStorageAsync();
        var record = MakeRecurring();

        await recurring.UpsertRecurringJobAsync(record);
        var all = await recurring.GetRecurringJobsAsync();

        var saved = all.Should().ContainSingle(r => r.RecurringJobId == record.RecurringJobId).Subject;
        saved.Cron.Should().Be(record.Cron);
        saved.Queue.Should().Be(record.Queue);
        saved.ConcurrencyPolicy.Should().Be(record.ConcurrencyPolicy);
    }

    [Fact]
    public async Task UpsertRecurringJobAsync_updates_existing_definition()
    {
        var (_, recurring, _, _) = await CreateStorageAsync();
        var id = $"upsert-test-{Guid.NewGuid()}";
        await recurring.UpsertRecurringJobAsync(MakeRecurring(id: id, cron: "* * * * *"));

        await recurring.UpsertRecurringJobAsync(MakeRecurring(id: id, cron: "0 9 * * *"));
        var all = await recurring.GetRecurringJobsAsync();

        all.Should().ContainSingle(r => r.RecurringJobId == id)
           .Which.Cron.Should().Be("0 9 * * *");
    }

    [Fact]
    public async Task GetDueRecurringJobsAsync_returns_only_due_jobs()
    {
        var (_, recurring, _, _) = await CreateStorageAsync();
        var dueId = $"due-{Guid.NewGuid()}";
        var futureId = $"future-{Guid.NewGuid()}";

        await recurring.UpsertRecurringJobAsync(
            MakeRecurring(id: dueId, nextExecution: DateTimeOffset.UtcNow.AddSeconds(-1)));
        await recurring.UpsertRecurringJobAsync(
            MakeRecurring(id: futureId, nextExecution: DateTimeOffset.UtcNow.AddHours(1)));

        var due = await recurring.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow);

        due.Should().Contain(r => r.RecurringJobId == dueId);
        due.Should().NotContain(r => r.RecurringJobId == futureId);
    }

    [Fact]
    public async Task SetRecurringJobNextExecutionAsync_persists_next_and_last_execution()
    {
        var (_, recurring, _, _) = await CreateStorageAsync();
        var record = MakeRecurring();
        await recurring.UpsertRecurringJobAsync(record);

        var next = DateTimeOffset.UtcNow.AddMinutes(5);
        await recurring.SetRecurringJobNextExecutionAsync(record.RecurringJobId, next);

        var all = await recurring.GetRecurringJobsAsync();
        var saved = all.Single(r => r.RecurringJobId == record.RecurringJobId);

        saved.NextExecution.Should().NotBeNull();
        saved.LastExecutedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SetRecurringJobLastExecutionResultAsync_records_success()
    {
        var (_, recurring, _, _) = await CreateStorageAsync();
        var record = MakeRecurring();
        await recurring.UpsertRecurringJobAsync(record);

        await recurring.SetRecurringJobLastExecutionResultAsync(
            record.RecurringJobId, JobStatus.Succeeded, errorMessage: null);

        var all = await recurring.GetRecurringJobsAsync();
        var saved = all.Single(r => r.RecurringJobId == record.RecurringJobId);

        saved.LastExecutionStatus.Should().Be(JobStatus.Succeeded);
        saved.LastExecutionError.Should().BeNull();
    }

    [Fact]
    public async Task SetRecurringJobLastExecutionResultAsync_records_failure_with_error()
    {
        var (_, recurring, _, _) = await CreateStorageAsync();
        var record = MakeRecurring();
        await recurring.UpsertRecurringJobAsync(record);

        await recurring.SetRecurringJobLastExecutionResultAsync(
            record.RecurringJobId, JobStatus.Failed, errorMessage: "timeout");

        var all = await recurring.GetRecurringJobsAsync();
        var saved = all.Single(r => r.RecurringJobId == record.RecurringJobId);

        saved.LastExecutionStatus.Should().Be(JobStatus.Failed);
        saved.LastExecutionError.Should().Be("timeout");
    }

    [Fact]
    public async Task DeleteRecurringJobAsync_removes_the_definition()
    {
        var (_, recurring, _, _) = await CreateStorageAsync();
        var record = MakeRecurring();
        await recurring.UpsertRecurringJobAsync(record);

        await recurring.DeleteRecurringJobAsync(record.RecurringJobId);
        var all = await recurring.GetRecurringJobsAsync();

        all.Should().NotContain(r => r.RecurringJobId == record.RecurringJobId);
    }

    // ── Server Tracking ────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterServerAsync_AddsServer_WhenSupported()
    {
        var (storage, _, _, _) = await CreateStorageAsync();
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
        var (storage, _, _, _) = await CreateStorageAsync();
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
        var (storage, _, _, _) = await CreateStorageAsync();
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
        var (storage, _, _, _) = await CreateStorageAsync();
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
        var (storage, _, dashboard, _) = await CreateStorageAsync();

        // Create and fetch parent job
        var parent = MakeJob();
        await storage.EnqueueAsync(parent);
        await Task.Delay(1);
        var parentFetched = (await storage.FetchNextAsync(["default"]))!;

        // Create child awaiting parent continuation
        var child = MakeJob(status: JobStatus.AwaitingContinuation, parentJobId: parentFetched.Id);
        await Task.Delay(1);
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
        var parentResult = await dashboard.GetJobByIdAsync(parentFetched.Id);
        parentResult!.Status.Should().Be(JobStatus.Succeeded);
        parentResult.ExecutionLogs.Should().HaveCount(1);

        var childResult = await dashboard.GetJobByIdAsync(child.Id);
        childResult!.Status.Should().Be(JobStatus.Enqueued);
    }

    [Fact]
    public async Task CommitJobResultAsync_Failure_WithRetry_SetsScheduled()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();

        var record = MakeJob();
        await storage.EnqueueAsync(record);
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

        var result = await dashboard.GetJobByIdAsync(fetched.Id);
        result!.Status.Should().Be(JobStatus.Scheduled);
        result.RetryAt.Should().BeCloseTo(retryAt, TimeSpan.FromMilliseconds(1));
        result.LastErrorMessage.Should().Contain("test failure");
        result.ExecutionLogs.Should().HaveCount(1);
    }

    [Fact]
    public async Task CommitJobResultAsync_Failure_NoRetry_SetsFailed()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();

        var record = MakeJob();
        await storage.EnqueueAsync(record);
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

        var result = await dashboard.GetJobByIdAsync(fetched.Id);
        result!.Status.Should().Be(JobStatus.Failed);
        result.CompletedAt.Should().NotBeNull();
        result.LastErrorMessage.Should().Contain("permanent failure");
    }

    [Fact]
    public async Task CommitJobResultAsync_IsIdempotent_WhenCalledTwice()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();

        var record = MakeJob();
        await storage.EnqueueAsync(record);
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

        var final = await dashboard.GetJobByIdAsync(fetched.Id);
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

    private static bool IsServerTrackingNotSupported(IJobStorage storage)
    {
        var name = storage.GetType().Name;
        return name.Contains("Redis") || name.Contains("SqlServer");
    }

    private static bool IsAtomicDedupNotSupported(IJobStorage storage)
    {
        var name = storage.GetType().Name;
        return name.Contains("Redis") || name.Contains("Mongo");
    }

    [Fact]
    public async Task PurgeJobsAsync_DeletesTerminalJobsBeyondRetention()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();

        // Use RetainSucceeded = 1 second to make test deterministic
        var policy = new RetentionPolicy
        {
            RetainSucceeded = TimeSpan.FromSeconds(1),
            RetainFailed = TimeSpan.Zero,
            RetainExpired = TimeSpan.Zero,
        };

        var record = MakeJob();
        await storage.EnqueueAsync(record);
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
        var remaining = await dashboard.GetJobByIdAsync(record.Id);
        remaining.Should().BeNull();
    }

    [Fact]
    public async Task PurgeJobsAsync_PreservesJobsWithinRetention()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();

        var policy = new RetentionPolicy
        {
            RetainSucceeded = TimeSpan.FromDays(7), // Long — won't purge
            RetainFailed = TimeSpan.Zero,
            RetainExpired = TimeSpan.Zero,
        };

        var record = MakeJob();
        await storage.EnqueueAsync(record);
        var fetched = await storage.FetchNextAsync(["default"]);
        await storage.CommitJobResultAsync(fetched!.Id, new JobExecutionResult
        {
            Succeeded = true,
            Logs = [],
        });

        var deleted = await storage.PurgeJobsAsync(policy);

        deleted.Should().Be(0);
        var remaining = await dashboard.GetJobByIdAsync(record.Id);
        remaining.Should().NotBeNull();
    }

    // ── DuplicatePolicy concurrency ────────────────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_ConcurrentWithSameIdempotencyKey_OnlyOneJobCreated()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();
        if (IsAtomicDedupNotSupported(storage))
        {
            return;
        }

        var key = $"concurrent-key-{Guid.NewGuid():N}";
        const int concurrency = 10;

        // Act — enqueue the same key from multiple concurrent tasks
        var tasks = Enumerable.Range(0, concurrency).Select(_ =>
        {
            var record = MakeJob(idempotencyKey: key);
            return storage.EnqueueAsync(record, DuplicatePolicy.AllowAfterFailed);
        });

        var results = await Task.WhenAll(tasks);

        // Assert — all results point to the same job
        var distinctJobIds = results.Select(r => r.JobId).Distinct().ToList();
        distinctJobIds.Should().HaveCount(1, "concurrent enqueues with same key must resolve to one job");

        // Only one job should exist in storage
        var filter = new JobFilter();
        var jobs = await dashboard.GetJobsAsync(filter, 1, 100);
        jobs.Items.Count(j => j.IdempotencyKey == key).Should().Be(1);
    }

    [Fact]
    public async Task EnqueueAsync_ConcurrentWithRejectAlways_AllRejectedAfterFirst()
    {
        var (storage, _, _, _) = await CreateStorageAsync();
        if (IsAtomicDedupNotSupported(storage))
        {
            return;
        }

        var key = $"reject-concurrent-{Guid.NewGuid():N}";

        // Seed a succeeded job with this key
        var seed = MakeJob(idempotencyKey: key);
        await storage.EnqueueAsync(seed, DuplicatePolicy.AllowAfterFailed);
        var fetched = await storage.FetchNextAsync(["default"]);
        await storage.CommitJobResultAsync(fetched!.Id, new JobExecutionResult
        {
            Succeeded = true,
            Logs = [],
        });

        // Act — concurrent re-enqueue with RejectAlways
        const int concurrency = 5;
        var tasks = Enumerable.Range(0, concurrency).Select(_ =>
        {
            var record = MakeJob(idempotencyKey: key);
            return storage.EnqueueAsync(record, DuplicatePolicy.RejectAlways);
        });

        var results = await Task.WhenAll(tasks);

        // Assert — all rejected, pointing to the original seeded job
        results.Should().AllSatisfy(r =>
        {
            r.WasRejected.Should().BeTrue();
            r.JobId.Should().Be(seed.Id);
        });
    }

    // ── Concurrency tests (race condition fixes) ────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_concurrent_same_idempotencyKey_creates_only_one_job()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();
        var key = "concurrent-race-test";

        // Act — fire multiple concurrent enqueues with the same idempotency key
        const int concurrency = 10;
        var tasks = Enumerable.Range(0, concurrency).Select(_ =>
        {
            var record = MakeJob(idempotencyKey: key);
            return storage.EnqueueAsync(record, DuplicatePolicy.AllowAfterFailed);
        });

        var results = await Task.WhenAll(tasks);

        // Assert — all should return the same JobId
        results.Select(r => r.JobId).Distinct().Should().HaveCount(1,
            "all concurrent enqueues with the same idempotency key should return the same JobId");

        // Assert — the winner job can be fetched and is consistent
        var winningJobId = results[0].JobId;
        var winningJob = await dashboard.GetJobByIdAsync(winningJobId);
        winningJob.Should().NotBeNull();
        winningJob!.IdempotencyKey.Should().Be(key);

        // All results should be non-rejected (first one creates, others see existing)
        results.Should().AllSatisfy(r =>
        {
            r.WasRejected.Should().BeFalse();
            r.JobId.Should().Be(results[0].JobId);
        });
    }

    [Fact]
    public async Task EnqueueAsync_concurrent_same_idempotencyKey_all_see_processing_status()
    {
        var (storage, _, dashboard, _) = await CreateStorageAsync();
        var key = "concurrent-processing-test";

        // Act — fire multiple concurrent enqueues with the same idempotency key
        const int concurrency = 5;
        var tasks = Enumerable.Range(0, concurrency).Select(_ =>
        {
            var record = MakeJob(idempotencyKey: key);
            return storage.EnqueueAsync(record, DuplicatePolicy.RejectIfFailed);
        });

        var results = await Task.WhenAll(tasks);

        // Assert — all should return the same JobId
        var winnerJobId = results[0].JobId;
        results.Should().AllSatisfy(r =>
        {
            r.JobId.Should().Be(winnerJobId);
            r.WasRejected.Should().BeFalse();
        });

        // Verify only one job exists
        var result = await dashboard.GetJobByIdAsync(winnerJobId);
        result.Should().NotBeNull();
        result!.IdempotencyKey.Should().Be(key);
        result.Status.Should().Be(JobStatus.Enqueued);
    }
}
