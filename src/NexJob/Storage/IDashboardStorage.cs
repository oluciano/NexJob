namespace NexJob.Storage;

/// <summary>
/// Read-heavy storage contract for dashboard queries and control actions.
/// Implementations MAY route these calls to a read replica.
/// </summary>
public interface IDashboardStorage
{
    /// <summary>Retrieves global job counts and throughput metrics.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Global job metrics.</returns>
    Task<JobMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>Retrieves a paginated list of jobs filtered by status, queue, or search string.</summary>
    /// <param name="filter">Filtering criteria.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A paged result of job records.</returns>
    Task<PagedResult<JobRecord>> GetJobsAsync(
        JobFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves a single job by its ID.</summary>
    /// <param name="id">The unique identifier of the job.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The job record if found; otherwise null.</returns>
    Task<JobRecord?> GetJobByIdAsync(JobId id, CancellationToken cancellationToken = default);

    /// <summary>Retrieves metrics broken down by queue (Enqueued, Processing count per queue).</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A collection of queue metrics.</returns>
    Task<IReadOnlyList<QueueMetrics>> GetQueueMetricsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes a job and its logs from storage.</summary>
    /// <param name="id">The unique identifier of the job.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeleteJobAsync(JobId id, CancellationToken cancellationToken = default);

    /// <summary>Resets a failed job to Enqueued state so it can be picked up again.</summary>
    /// <param name="id">The unique identifier of the job.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task RequeueJobAsync(JobId id, CancellationToken cancellationToken = default);

    /// <summary>Persists execution logs captured during job invocation.</summary>
    /// <param name="jobId">The unique identifier of the job.</param>
    /// <param name="logs">Collection of log entries to save.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SaveExecutionLogsAsync(
        JobId jobId,
        IReadOnlyList<JobExecutionLog> logs,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves jobs that have the specified tag.</summary>
    /// <param name="tag">The tag to search for.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A collection of matching job records.</returns>
    Task<IReadOnlyList<JobRecord>> GetJobsByTagAsync(
        string tag,
        CancellationToken cancellationToken = default);
}
