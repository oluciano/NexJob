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
    /// <remarks>
    /// A job that is already running is not interrupted: it finishes, and its result, heartbeat and progress are
    /// discarded. Deleting never brings the job back.
    /// </remarks>
    /// <param name="id">The unique identifier of the job.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task DeleteJobAsync(JobId id, CancellationToken ct = default);

    /// <summary>
    /// Pauses a queue. Workers will skip this queue until resumed.
    /// </summary>
    /// <remarks>
    /// The pause is read by each worker node on its next polling cycle, so it takes effect within one cycle, not at
    /// the instant of the call: a job already fetched by a cycle that was in flight when the pause was stored still
    /// runs. Jobs that are already running are never interrupted.
    /// </remarks>
    /// <param name="queue">The name of the queue to pause. Pausing <c>default</c> also pauses the default queue of the application (<c>{prefix}.default</c>).</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task PauseQueueAsync(string queue, CancellationToken ct = default);

    /// <summary>
    /// Resumes a paused queue.
    /// </summary>
    /// <param name="queue">The name of the queue to resume. <c>default</c> and the default queue of the application (<c>{prefix}.default</c>) are one queue here: resuming either name resumes both, whichever name paused it.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task ResumeQueueAsync(string queue, CancellationToken ct = default);

    /// <summary>
    /// Resets the circuit breaker state of a queue to <see cref="Configuration.QueueCircuitState.Closed"/>,
    /// clearing consecutive failure counters and cooldown timers.
    /// </summary>
    /// <param name="queue">The name of the queue whose circuit breaker will be reset. <c>default</c> resets the circuit of the default queue of the application (<c>{prefix}.default</c>).</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task ResetQueueCircuitAsync(string queue, CancellationToken ct = default);
}
