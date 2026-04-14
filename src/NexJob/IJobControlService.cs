namespace NexJob;

/// <summary>
/// Provides programmatic control over jobs and queues.
/// Inject this service to requeue, delete, or pause jobs outside the dashboard.
/// </summary>
public interface IJobControlService
{
    /// <summary>
    /// Re-enqueues a failed (dead-letter) job. Resets attempt count to 0.
    /// </summary>
    /// <param name="id">The unique identifier of the job.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task RequeueJobAsync(JobId id, CancellationToken ct = default);

    /// <summary>
    /// Permanently deletes a job record and its logs.
    /// </summary>
    /// <param name="id">The unique identifier of the job.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task DeleteJobAsync(JobId id, CancellationToken ct = default);

    /// <summary>
    /// Pauses a queue. Workers will skip this queue until resumed.
    /// </summary>
    /// <param name="queue">The name of the queue to pause.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task PauseQueueAsync(string queue, CancellationToken ct = default);

    /// <summary>
    /// Resumes a paused queue.
    /// </summary>
    /// <param name="queue">The name of the queue to resume.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task ResumeQueueAsync(string queue, CancellationToken ct = default);
}
