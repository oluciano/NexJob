namespace NexJob.Storage;

/// <summary>
/// Hot-path storage contract for job execution and worker coordination.
/// Implementations must guarantee atomic dequeue via SELECT FOR UPDATE SKIP LOCKED
/// or equivalent. Used by the dispatcher, scheduler, and orphan watcher.
/// </summary>
public interface IJobStorage
{
    /// <summary>Enqueues a new job for execution.</summary>
    /// <param name="job">The job record to persist.</param>
    /// <param name="duplicatePolicy">How to handle existing jobs with the same idempotency key.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The result of the enqueue operation, including the assigned JobId.</returns>
    Task<EnqueueResult> EnqueueAsync(
        JobRecord job,
        DuplicatePolicy duplicatePolicy = DuplicatePolicy.AllowAfterFailed,
        CancellationToken cancellationToken = default);

    /// <summary>Fetches the next available job from the specified queues.</summary>
    /// <param name="queues">List of queue names to poll, in priority order.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A job record if found; otherwise null.</returns>
    Task<JobRecord?> FetchNextAsync(
        IReadOnlyList<string> queues,
        CancellationToken cancellationToken = default);

    /// <summary>Marks a job as successfully completed.</summary>
    /// <param name="jobId">The unique identifier of the job.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task AcknowledgeAsync(JobId jobId, CancellationToken cancellationToken = default);

    /// <summary>Marks a job as failed, potentially scheduling it for retry.</summary>
    /// <param name="jobId">The unique identifier of the job.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <param name="retryAt">Timestamp for the next retry attempt, or null if no more retries.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SetFailedAsync(
        JobId jobId,
        Exception exception,
        DateTimeOffset? retryAt,
        CancellationToken cancellationToken = default);

    /// <summary>Marks a job as expired due to deadline enforcement.</summary>
    /// <param name="jobId">The unique identifier of the job.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SetExpiredAsync(JobId jobId, CancellationToken cancellationToken = default);

    /// <summary>Updates the heartbeat timestamp for an active job to prevent it from being marked as orphaned.</summary>
    /// <param name="jobId">The unique identifier of the job.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task UpdateHeartbeatAsync(JobId jobId, CancellationToken cancellationToken = default);

    /// <summary>Atomically commits the final result of a job execution.</summary>
    /// <param name="jobId">The unique identifier of the job.</param>
    /// <param name="result">The complete execution outcome.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task CommitJobResultAsync(
        JobId jobId,
        JobExecutionResult result,
        CancellationToken cancellationToken = default);

    /// <summary>Scans for and requeues jobs that have been in Processing state longer than the heartbeat timeout.</summary>
    /// <param name="heartbeatTimeout">Maximum allowed time since the last heartbeat.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task RequeueOrphanedJobsAsync(
        TimeSpan heartbeatTimeout,
        CancellationToken cancellationToken = default);

    /// <summary>Enqueues any continuations (child jobs) that were waiting for the specified parent job to complete.</summary>
    /// <param name="parentJobId">The unique identifier of the parent job.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task EnqueueContinuationsAsync(
        JobId parentJobId,
        CancellationToken cancellationToken = default);

    /// <summary>Reports progress for an active job.</summary>
    /// <param name="jobId">The unique identifier of the job.</param>
    /// <param name="percent">Progress percentage (0-100).</param>
    /// <param name="message">Optional status message.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task ReportProgressAsync(
        JobId jobId,
        int percent,
        string? message,
        CancellationToken ct = default);

    /// <summary>Permanently deletes terminal jobs (Succeeded, Failed, Expired) based on the specified retention policy.</summary>
    /// <param name="policy">Retention thresholds for each terminal state.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The number of jobs purged.</returns>
    Task<int> PurgeJobsAsync(
        RetentionPolicy policy,
        CancellationToken cancellationToken = default);

    /// <summary>Registers a new worker node (server) in the cluster.</summary>
    /// <param name="server">Metadata about the server node.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task RegisterServerAsync(ServerRecord server, CancellationToken cancellationToken = default);

    /// <summary>Updates the heartbeat timestamp for an active server node.</summary>
    /// <param name="serverId">Unique identifier of the server.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task HeartbeatServerAsync(string serverId, CancellationToken cancellationToken = default);

    /// <summary>Removes a server node from the active registry.</summary>
    /// <param name="serverId">Unique identifier of the server.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeregisterServerAsync(string serverId, CancellationToken cancellationToken = default);

    /// <summary>Retrieves a list of all active server nodes in the cluster.</summary>
    /// <param name="activeTimeout">Maximum allowed time since the last server heartbeat.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A collection of active server records.</returns>
    Task<IReadOnlyList<ServerRecord>> GetActiveServersAsync(
        TimeSpan activeTimeout,
        CancellationToken cancellationToken = default);
}
