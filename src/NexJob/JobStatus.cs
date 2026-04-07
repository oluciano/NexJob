namespace NexJob;

/// <summary>
/// Lifecycle states of a persisted job.
/// </summary>
/// <remarks>
/// <para>
/// Terminal states (cannot transition further): <see cref="Succeeded"/>, <see cref="Failed"/>, <see cref="Expired"/>, <see cref="Deleted"/>.
/// </para>
/// <para>
/// Transient states (job will keep moving): <see cref="Enqueued"/>, <see cref="Scheduled"/>, <see cref="Processing"/>, <see cref="AwaitingContinuation"/>.
/// </para>
/// <para>
/// Normal happy path: <see cref="Enqueued"/> → <see cref="Processing"/> → <see cref="Succeeded"/>.
/// </para>
/// <para>
/// Retry path: <see cref="Processing"/> → <see cref="Enqueued"/> (retry) → <see cref="Processing"/> → ... → <see cref="Failed"/>.
/// </para>
/// <para>
/// Deadline path: <see cref="Enqueued"/> → <see cref="Expired"/>.
/// </para>
/// </remarks>
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
    /// <remarks>
    /// This is a terminal state — the job will not execute again.
    /// A <see cref="IDeadLetterHandler{TJob}"/> is invoked when this state is reached,
    /// if one is registered. Jobs with pending retries use <see cref="Enqueued"/> instead.
    /// </remarks>
    Failed,

    /// <summary>The job was explicitly deleted before execution.</summary>
    Deleted,

    /// <summary>The job is waiting for its parent job to complete.</summary>
    AwaitingContinuation,

    /// <summary>The job was not executed before its deadline and was discarded.</summary>
    /// <remarks>
    /// Expiry is intentional and observable — the job is marked <see cref="Expired"/>,
    /// visible in the dashboard, and tracked via the <c>nexjob.jobs.expired</c> metric.
    /// Unlike execution failures, expiry does not trigger retry or dead-letter handling.
    /// If you did not set <c>deadlineAfter</c>, this state will never occur.
    /// </remarks>
    Expired,
}
