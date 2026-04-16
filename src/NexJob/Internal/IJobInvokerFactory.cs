namespace NexJob.Internal;

/// <summary>
/// Prepares all artifacts needed to invoke a single job execution:
/// creates the DI scope, resolves types, migrates payload, deserializes
/// input, instantiates the job, and compiles the invoker delegate.
/// </summary>
internal interface IJobInvokerFactory
{
    /// <summary>
    /// Prepares the invocation context for the given job record.
    /// The returned context owns the DI scope - caller must dispose it.
    /// </summary>
    /// <param name="job">The persisted job record to prepare.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The prepared invocation context.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the job type or input type cannot be resolved,
    /// or if deserialization produces a null input.
    /// </exception>
    Task<JobInvocationContext> PrepareAsync(JobRecord job, CancellationToken ct = default);
}
