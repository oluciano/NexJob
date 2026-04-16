namespace NexJob.Internal;

/// <summary>
/// Resolves and invokes the <see cref="IDeadLetterHandler{TJob}"/> registered
/// for a failed job, if one exists.
/// Exceptions thrown by the handler are swallowed; dead-letter handlers
/// must never crash the dispatcher.
/// </summary>
internal interface IDeadLetterDispatcher
{
    /// <summary>
    /// Attempts to invoke the dead-letter handler for the given job.
    /// If no handler is registered for the job type, this is a no-op.
    /// If the handler throws, the exception is logged and swallowed.
    /// </summary>
    /// <param name="job">The failed job record.</param>
    /// <param name="lastException">The exception from the final failed attempt.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous dispatch operation.</returns>
    Task DispatchAsync(JobRecord job, Exception lastException, CancellationToken ct = default);
}
