namespace NexJob;

/// <summary>
/// Lifecycle states of a persisted job.
/// </summary>
public enum JobStatus
{
    /// <summary>The job is waiting to be picked up by a worker.</summary>
    Enqueued,

    /// <summary>The job is scheduled to run at a future point in time.</summary>
    Scheduled,

    /// <summary>A worker has claimed the job and is currently executing it.</summary>
    Processing,

    /// <summary>The job completed successfully.</summary>
    Succeeded,

    /// <summary>The job exhausted all retry attempts and moved to the dead-letter state.</summary>
    Failed,

    /// <summary>The job was explicitly deleted before execution.</summary>
    Deleted,

    /// <summary>The job is waiting for its parent job to complete.</summary>
    AwaitingContinuation,

    /// <summary>The job was not executed before its deadline and was discarded.</summary>
    Expired,
}
