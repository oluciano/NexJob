namespace NexJob;

/// <summary>
/// Reacts to a job that exhausted its retries, independently of the job's own <see cref="IDeadLetterHandler{TJob}"/>.
/// Every registered forwarder whose <see cref="AppliesTo"/> returns <see langword="true"/> is called after the typed
/// handler, so a handler written for a job type never prevents a forwarder from running.
/// </summary>
/// <remarks>
/// Intended for integrations that copy a failed job somewhere else (for example the Kafka and RabbitMQ triggers).
/// Exceptions thrown by a forwarder are logged and swallowed, like those of dead-letter handlers.
/// </remarks>
public interface IDeadLetterForwarder
{
    /// <summary>
    /// Decides whether this forwarder wants the given failed job.
    /// </summary>
    /// <param name="failedJob">The job record that exhausted its retries.</param>
    /// <returns><see langword="true"/> to receive the job in <see cref="ForwardAsync"/>.</returns>
    bool AppliesTo(JobRecord failedJob);

    /// <summary>
    /// Called once for a failed job this forwarder applies to.
    /// </summary>
    /// <param name="failedJob">The job record, including the input payload and last error.</param>
    /// <param name="lastException">The exception from the final failed attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the job has been handed over.</returns>
    Task ForwardAsync(
        JobRecord failedJob,
        Exception lastException,
        CancellationToken cancellationToken);
}
