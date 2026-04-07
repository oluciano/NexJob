namespace NexJob.Exceptions;

/// <summary>
/// Exception thrown by <see cref="IScheduler"/> when a job enqueue is rejected
/// because a duplicate job with the same idempotency key already exists and
/// the configured <see cref="DuplicatePolicy"/> does not allow re-enqueuing.
/// </summary>
public sealed class DuplicateJobException : System.InvalidOperationException
{
    /// <summary>
    /// Initializes a new <see cref="DuplicateJobException"/>.
    /// </summary>
    public DuplicateJobException(string idempotencyKey, JobId existingJobId, DuplicatePolicy policy)
        : base($"Job with idempotency key '{idempotencyKey}' was rejected by policy '{policy}'. Existing job: {existingJobId.Value}.")
    {
        IdempotencyKey = idempotencyKey;
        ExistingJobId = existingJobId;
        Policy = policy;
    }

    /// <summary>
    /// Gets the idempotency key that caused the rejection.
    /// </summary>
    public string IdempotencyKey { get; }

    /// <summary>
    /// Gets the identifier of the existing job that triggered the duplicate check.
    /// </summary>
    public JobId ExistingJobId { get; }

    /// <summary>
    /// Gets the policy that rejected the enqueue.
    /// </summary>
    public DuplicatePolicy Policy { get; }
}
