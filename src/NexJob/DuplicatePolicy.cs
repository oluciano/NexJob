namespace NexJob;

/// <summary>
/// Controls the behaviour when a job with the same <c>idempotencyKey</c> already exists
/// in a terminal failure state (<see cref="JobStatus.Failed"/>).
/// </summary>
/// <remarks>
/// This policy only applies when an <c>idempotencyKey</c> is supplied and an existing job
/// with that key is found in <see cref="JobStatus.Failed"/> state.
/// Jobs in active states (<see cref="JobStatus.Enqueued"/>, <see cref="JobStatus.Processing"/>,
/// <see cref="JobStatus.Scheduled"/>, <see cref="JobStatus.AwaitingContinuation"/>) are always
/// deduplicated regardless of this policy.
/// </remarks>
public enum DuplicatePolicy
{
    /// <summary>
    /// A new job is created even if a previous job with the same key has permanently failed.
    /// This is the default behaviour and supports at-least-once execution semantics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WARNING: This policy does not prevent duplicate side effects. If the original job
    /// partially succeeded before failing, a new execution may repeat those effects.
    /// Ensure your job implementation is idempotent before relying on this policy.
    /// </para>
    /// </remarks>
    AllowAfterFailed = 0,

    /// <summary>
    /// The enqueue is rejected if a previous job with the same key has permanently failed.
    /// Use this policy when duplicate execution after failure would cause unacceptable side effects.
    /// </summary>
    RejectIfFailed = 1,

    /// <summary>
    /// The enqueue is always rejected if any job with the same key exists in any terminal state
    /// (<see cref="JobStatus.Succeeded"/>, <see cref="JobStatus.Failed"/>, <see cref="JobStatus.Expired"/>).
    /// Use this policy to guarantee exactly-once execution across the full job lifetime.
    /// </summary>
    RejectAlways = 2,
}
