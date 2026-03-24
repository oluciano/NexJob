namespace NexJob;

/// <summary>
/// Defines a background job that processes a strongly-typed input.
/// Implement this interface to create a NexJob job.
/// </summary>
public interface IJob<TInput>
{
    Task ExecuteAsync(TInput input, CancellationToken cancellationToken);
}
