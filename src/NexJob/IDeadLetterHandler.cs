namespace NexJob;

/// <summary>
/// Handles a job that has exhausted all retry attempts and is being moved
/// to the dead-letter state.
/// Implement this interface to define automatic fallback logic — send an alert,
/// write to an audit log, enqueue a compensating job, or notify an external system.
/// </summary>
/// <typeparam name="TJob">The job type that failed definitively. Used only for DI resolution.</typeparam>
#pragma warning disable S2326 // Unused type parameter - TJob is used for DI resolution
public interface IDeadLetterHandler<TJob>
#pragma warning restore S2326
{
    /// <summary>
    /// Called when <typeparamref name="TJob"/> has exhausted all retry attempts.
    /// </summary>
    /// <param name="failedJob">
    /// The job record, including input payload, attempt count, and last error.
    /// </param>
    /// <param name="lastException">The exception from the final failed attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task HandleAsync(
        JobRecord failedJob,
        Exception lastException,
        CancellationToken cancellationToken);
}
