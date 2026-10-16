using NexJob.Storage;

namespace NexJob.Internal;

/// <summary>
/// The duplicate-handling rules shared by every storage provider. They depend only on the status of the existing job
/// and the <see cref="DuplicatePolicy"/>, never on the database, so they live here once and the providers keep only the I/O.
/// </summary>
internal static class DuplicateRules
{
    /// <summary>Gets whether a job in this status is still going to run, so a duplicate is never a new job.</summary>
    /// <param name="status">The status of the existing job.</param>
    /// <returns><c>true</c> when the job is enqueued, processing, scheduled or awaiting a continuation.</returns>
    public static bool IsActive(JobStatus status) =>
        status is JobStatus.Enqueued or JobStatus.Processing or JobStatus.Scheduled or JobStatus.AwaitingContinuation;

    /// <summary>Decides what to do with an enqueue whose idempotency key matches an existing job.</summary>
    /// <param name="existingId">The id of the existing job.</param>
    /// <param name="existingStatus">The status of the existing job.</param>
    /// <param name="policy">The policy of the new enqueue.</param>
    /// <returns>The existing id, rejected when the policy says the new job must not be created.</returns>
    public static EnqueueResult Resolve(JobId existingId, JobStatus existingStatus, DuplicatePolicy policy)
    {
        if (IsActive(existingStatus))
        {
            return new EnqueueResult(existingId, WasRejected: false);
        }

        var wasRejected = existingStatus == JobStatus.Failed
            ? policy is DuplicatePolicy.RejectIfFailed or DuplicatePolicy.RejectAlways
            : policy == DuplicatePolicy.RejectAlways;

        return new EnqueueResult(existingId, WasRejected: wasRejected);
    }
}
