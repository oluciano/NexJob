using NexJob.Configuration;

namespace NexJob.Internal;

/// <summary>
/// Contract for managing and inspecting dynamic circuit breakers per queue.
/// </summary>
public interface IQueueCircuitBreakerManager
{
    /// <summary>
    /// Gets the current circuit state and permitted concurrency for a given queue.
    /// </summary>
    /// <param name="queue">The queue name.</param>
    /// <param name="allowedConcurrency">Outputs the permitted concurrent executions for this queue.</param>
    /// <returns>The current <see cref="QueueCircuitState"/>.</returns>
    QueueCircuitState GetState(string queue, out int allowedConcurrency);

    /// <summary>
    /// Records the execution outcome of a job in a given queue.
    /// </summary>
    /// <param name="queue">The queue name.</param>
    /// <param name="succeeded"><see langword="true"/> if the execution was successful.</param>
    /// <param name="exception">The exception thrown, or <see langword="null"/> if successful.</param>
    void RecordOutcome(string queue, bool succeeded, Exception? exception);

    /// <summary>
    /// Manually resets the circuit breaker for a given queue to <see cref="QueueCircuitState.Closed"/>.
    /// </summary>
    /// <param name="queue">The queue name.</param>
    void Reset(string queue);

    /// <summary>
    /// Gets detailed circuit breaker status for all configured queues.
    /// </summary>
    /// <returns>Collection of queue circuit statuses.</returns>
    IReadOnlyList<QueueCircuitStatus> GetAllStatuses();

    /// <summary>
    /// Gets detailed circuit breaker status for a specific queue, or <see langword="null"/> if not configured.
    /// </summary>
    /// <param name="queue">The queue name.</param>
    /// <returns>The status or <see langword="null"/>.</returns>
    QueueCircuitStatus? GetStatus(string queue);
}
