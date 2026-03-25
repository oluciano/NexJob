namespace NexJob;

/// <summary>
/// Defines a background job that processes a strongly-typed input.
/// Implement this interface to create a NexJob job.
/// </summary>
/// <typeparam name="TInput">The type of the input payload for this job.</typeparam>
public interface IJob<in TInput>
{
    /// <summary>
    /// Executes the background job with the provided input.
    /// </summary>
    /// <param name="input">The strongly-typed input payload for this job.</param>
    /// <param name="cancellationToken">
    /// Token that is cancelled when the host is shutting down. Implementations should
    /// propagate this token to all async I/O calls.
    /// </param>
    Task ExecuteAsync(TInput input, CancellationToken cancellationToken);
}
