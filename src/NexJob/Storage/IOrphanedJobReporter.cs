namespace NexJob.Storage;

/// <summary>
/// Optional capability of an <see cref="IJobStorage"/>: requeue orphaned jobs and report the ones that were failed
/// because they had no attempts left, so the orphan watcher can run dead-letter handling for them.
/// A provider that does not implement it keeps working: crash-exhausted jobs still become
/// <see cref="JobStatus.Failed"/>, but no dead-letter handler or forwarder is called for them.
/// </summary>
public interface IOrphanedJobReporter
{
    /// <summary>
    /// Does what <see cref="IJobStorage.RequeueOrphanedJobsAsync"/> does and returns the jobs this call moved to
    /// <see cref="JobStatus.Failed"/>. A job is reported by the call that moved it and by no other call, so several
    /// nodes running the watcher never report the same job twice.
    /// </summary>
    /// <param name="heartbeatTimeout">Maximum allowed time since the last heartbeat.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The ids of the jobs failed by this call.</returns>
    Task<IReadOnlyList<JobId>> RequeueOrphanedJobsAndReportAsync(
        TimeSpan heartbeatTimeout,
        CancellationToken cancellationToken = default);
}
