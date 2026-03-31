namespace NexJob.Storage;

/// <summary>
/// Abstraction over the persistence layer.
/// Implement this interface to create a custom NexJob storage adapter.
/// </summary>
/// <remarks>
/// All implementations must guarantee that <see cref="FetchNextAsync"/> performs an atomic
/// dequeue — equivalent to <c>SELECT FOR UPDATE SKIP LOCKED</c> in SQL — so that a job is
/// never claimed by two workers simultaneously.
/// </remarks>
public interface IStorageProvider
{
    /// <summary>
    /// Persists a new job record and, if it is immediately eligible (status =
    /// <see cref="JobStatus.Enqueued"/>), makes it available for workers to claim.
    /// </summary>
    /// <param name="job">The job record to persist.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// The <see cref="JobId"/> of the persisted job, or the <see cref="JobId"/> of the
    /// existing job when an idempotency key collision is detected.
    /// </returns>
    Task<JobId> EnqueueAsync(JobRecord job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically claims the next available job from the specified queues and marks it
    /// as <see cref="JobStatus.Processing"/>. Returns <see langword="null"/> when no
    /// eligible job is available.
    /// </summary>
    /// <param name="queues">
    /// Ordered list of queue names to poll. Queues are checked in the order supplied,
    /// and within each queue jobs are ordered by <see cref="JobPriority"/> then creation time.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<JobRecord?> FetchNextAsync(IReadOnlyList<string> queues, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a successfully completed job as <see cref="JobStatus.Succeeded"/>.
    /// </summary>
    /// <param name="jobId">The identifier of the completed job.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task AcknowledgeAsync(JobId jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a job failure and either schedules a retry or permanently marks the
    /// job as <see cref="JobStatus.Failed"/> (dead-letter) when no retry is requested.
    /// </summary>
    /// <param name="jobId">The identifier of the failed job.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <param name="retryAt">
    /// UTC timestamp for the next retry attempt, or <see langword="null"/> to move the
    /// job to the dead-letter state with no further retries.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SetFailedAsync(JobId jobId, Exception exception, DateTimeOffset? retryAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a job as <see cref="JobStatus.Expired"/> because its deadline passed
    /// before execution began.
    /// </summary>
    /// <param name="jobId">The identifier of the expired job.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SetExpiredAsync(JobId jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the heartbeat timestamp for a job that is currently being processed.
    /// Workers call this periodically so that the orphan watcher can distinguish
    /// active workers from crashed ones.
    /// </summary>
    /// <param name="jobId">The identifier of the job whose heartbeat should be refreshed.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task UpdateHeartbeatAsync(JobId jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a recurring job definition.
    /// </summary>
    /// <param name="recurringJob">The recurring job definition to persist.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task UpsertRecurringJobAsync(RecurringJobRecord recurringJob, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all recurring job definitions whose <see cref="RecurringJobRecord.NextExecution"/>
    /// is on or before <paramref name="utcNow"/>.
    /// </summary>
    /// <param name="utcNow">The current UTC instant used as the evaluation cutoff.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<IReadOnlyList<RecurringJobRecord>> GetDueRecurringJobsAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the <see cref="RecurringJobRecord.NextExecution"/> and
    /// <see cref="RecurringJobRecord.LastExecutedAt"/> fields for a recurring job.
    /// </summary>
    /// <param name="recurringJobId">The identifier of the recurring job to update.</param>
    /// <param name="nextExecution">The next UTC instant at which the job should fire.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SetRecurringJobNextExecutionAsync(string recurringJobId, DateTimeOffset nextExecution, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the outcome of the most recent execution of a recurring job — updates
    /// <see cref="RecurringJobRecord.LastExecutionStatus"/> and, on failure,
    /// <see cref="RecurringJobRecord.LastExecutionError"/>.
    /// </summary>
    /// <param name="recurringJobId">The identifier of the recurring job.</param>
    /// <param name="status">The final status of the executed instance (<see cref="JobStatus.Succeeded"/> or <see cref="JobStatus.Failed"/>).</param>
    /// <param name="errorMessage">Error message if the job failed; <see langword="null"/> on success.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SetRecurringJobLastExecutionResultAsync(string recurringJobId, JobStatus status, string? errorMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently removes a recurring job definition. Already-enqueued instances
    /// of the job are not affected.
    /// </summary>
    /// <param name="recurringJobId">The identifier of the recurring job to delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeleteRecurringJobAsync(string recurringJobId, CancellationToken cancellationToken = default);

    /// <summary>Updates the cron override and enabled flag for a recurring job.</summary>
    /// <param name="recurringJobId">The identifier of the recurring job to update.</param>
    /// <param name="cronOverride">
    /// User-supplied cron expression that overrides the default schedule,
    /// or <see langword="null"/> to clear any existing override.
    /// </param>
    /// <param name="enabled">
    /// When <see langword="false"/> the scheduler will skip this job at every firing.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task UpdateRecurringJobConfigAsync(string recurringJobId, string? cronOverride, bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a recurring job by setting <see cref="RecurringJobRecord.DeletedByUser"/> to
    /// <see langword="true"/> and <see cref="RecurringJobRecord.Enabled"/> to <see langword="false"/>.
    /// The scheduler will skip the job at every firing and a subsequent call to
    /// <see cref="UpsertRecurringJobAsync"/> will not resurrect it. All associated job records are
    /// permanently removed. Use <see cref="RestoreRecurringJobAsync"/> to reverse the deletion.
    /// </summary>
    /// <param name="recurringJobId">The identifier of the recurring job to soft-delete.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ForceDeleteRecurringJobAsync(string recurringJobId, CancellationToken cancellationToken = default);

    /// <summary>Restores a recurring job that was soft-deleted via <see cref="ForceDeleteRecurringJobAsync"/>.</summary>
    /// <param name="recurringJobId">The identifier of the recurring job to restore.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task RestoreRecurringJobAsync(string recurringJobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all registered recurring job definitions, regardless of their next execution time.
    /// Used by the dashboard to display the full list of recurring jobs.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<IReadOnlyList<RecurringJobRecord>> GetRecurringJobsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the recurring job definition with the specified identifier, or <see langword="null"/> if not found.</summary>
    /// <param name="recurringJobId">The identifier of the recurring job to look up.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<RecurringJobRecord?> GetRecurringJobByIdAsync(string recurringJobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds all jobs in <see cref="JobStatus.Processing"/> state whose heartbeat has
    /// not been refreshed within <paramref name="heartbeatTimeout"/>, and re-enqueues
    /// them for execution by a healthy worker.
    /// </summary>
    /// <param name="heartbeatTimeout">
    /// Maximum acceptable time since the last heartbeat. Jobs silent for longer than
    /// this duration are assumed to have been abandoned by a crashed worker.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task RequeueOrphanedJobsAsync(TimeSpan heartbeatTimeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds all jobs in <see cref="JobStatus.AwaitingContinuation"/> whose parent job
    /// matches <paramref name="parentJobId"/>, and transitions them to
    /// <see cref="JobStatus.Enqueued"/> so they are picked up by workers.
    /// </summary>
    /// <param name="parentJobId">The identifier of the successfully completed parent job.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task EnqueueContinuationsAsync(JobId parentJobId, CancellationToken cancellationToken = default);

    // ── Server / Worker node tracking ─────────────────────────────────────────

    /// <summary>
    /// Registers or updates an active worker node in the cluster.
    /// </summary>
    /// <param name="server">The server details to register or update.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task RegisterServerAsync(ServerRecord server, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes the heartbeat timestamp of the specified server node.
    /// </summary>
    /// <param name="serverId">The identifier of the server.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task HeartbeatServerAsync(string serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gracefully removes the specified server node from the active registry.
    /// </summary>
    /// <param name="serverId">The identifier of the server to remove.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeregisterServerAsync(string serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a list of all active server nodes that have sent a heartbeat
    /// within the last <paramref name="activeTimeout"/> period.
    /// </summary>
    /// <param name="activeTimeout">The threshold defining when a server is considered offline.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<IReadOnlyList<ServerRecord>> GetActiveServersAsync(TimeSpan activeTimeout, CancellationToken cancellationToken = default);

    // ── Dashboard support ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns an aggregated metrics snapshot used by the dashboard overview page.
    /// </summary>
    Task<JobMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a paginated, filtered list of job records.
    /// </summary>
    /// <param name="filter">Status, search text, and queue filters.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Maximum number of records per page.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<PagedResult<JobRecord>> GetJobsAsync(JobFilter filter, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single job record by its identifier, or <see langword="null"/> if not found.
    /// </summary>
    Task<JobRecord?> GetJobByIdAsync(JobId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently removes a job record. Used by the dashboard Delete action.
    /// </summary>
    Task DeleteJobAsync(JobId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-enqueues a failed (dead-letter) job so it will be retried.
    /// Resets <see cref="JobRecord.Attempts"/> to 0 and sets status to
    /// <see cref="JobStatus.Enqueued"/>. Used by the dashboard Requeue action.
    /// </summary>
    Task RequeueJobAsync(JobId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the log entries captured during the most recent execution of a job.
    /// Replaces any previously stored logs for the same job.
    /// </summary>
    /// <param name="jobId">The identifier of the job whose logs should be saved.</param>
    /// <param name="logs">Captured log entries to persist.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SaveExecutionLogsAsync(JobId jobId, IReadOnlyList<JobExecutionLog> logs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns per-queue metrics (enqueued + processing counts) for all active queues.
    /// </summary>
    Task<IReadOnlyList<QueueMetrics>> GetQueueMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to acquire an exclusive lock for scheduling a recurring job.
    /// Returns <see langword="true"/> if the lock was acquired (caller should enqueue the job).
    /// Returns <see langword="false"/> if another instance already holds the lock.
    /// The lock expires automatically after <paramref name="ttl"/>.
    /// </summary>
    /// <param name="recurringJobId">The identifier of the recurring job to lock.</param>
    /// <param name="ttl">How long the lock remains valid before expiring automatically.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<bool> TryAcquireRecurringJobLockAsync(string recurringJobId, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>Releases the recurring job scheduling lock.</summary>
    /// <param name="recurringJobId">The identifier of the recurring job whose lock should be released.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task ReleaseRecurringJobLockAsync(string recurringJobId, CancellationToken ct = default);

    /// <summary>
    /// Records the current execution progress of a job.
    /// Called by <see cref="IJobContext.ReportProgressAsync"/>.
    /// </summary>
    /// <param name="jobId">The job being tracked.</param>
    /// <param name="percent">Progress percentage, 0–100.</param>
    /// <param name="message">Optional human-readable status message.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ReportProgressAsync(JobId jobId, int percent, string? message, CancellationToken ct = default);

    /// <summary>
    /// Returns all jobs that have the specified tag attached.
    /// </summary>
    /// <param name="tag">The tag to filter by.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<IReadOnlyList<JobRecord>> GetJobsByTagAsync(string tag, CancellationToken cancellationToken = default);
}
