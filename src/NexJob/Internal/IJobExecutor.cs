namespace NexJob.Internal;

/// <summary>
/// Runs a single fetched job to completion: deadline check, invocation, retry or dead-letter decision and the commit
/// of its result. The dispatcher depends on this seam so its polling loop and worker pool can be tested with a fake.
/// </summary>
internal interface IJobExecutor
{
    /// <summary>
    /// Executes the job and persists its outcome.
    /// </summary>
    /// <param name="job">The job that was fetched from storage.</param>
    /// <param name="cancellationToken">A cancellation token that is signalled when the host is shutting down.</param>
    /// <returns>A task that completes when the job outcome has been committed.</returns>
    Task ExecuteJobAsync(JobRecord job, CancellationToken cancellationToken = default);
}
