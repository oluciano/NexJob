namespace NexJob.Storage;

/// <summary>
/// Result returned by <see cref="IJobStorage.EnqueueAsync"/> containing the job identifier
/// and a flag indicating whether the enqueue was rejected due to a <see cref="DuplicatePolicy"/>.
/// </summary>
/// <param name="JobId">The identifier of the enqueued job, or the existing job if deduplicated.</param>
/// <param name="WasRejected">
/// <see langword="true"/> when the enqueue was rejected by a <see cref="DuplicatePolicy"/> rule.
/// The <see cref="JobId"/> still points to the existing job that caused the rejection.
/// </param>
public sealed record EnqueueResult(JobId JobId, bool WasRejected);
