namespace NexJob.Storage;

/// <summary>
/// Encapsulates the complete outcome of a single job execution.
/// Passed to <see cref="IStorageProvider.CommitJobResultAsync"/> to persist
/// all state transitions atomically.
/// </summary>
public sealed class JobExecutionResult
{
    /// <summary>Whether the job succeeded or failed.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>Log entries captured during execution. May be empty.</summary>
    public required IReadOnlyList<JobExecutionLog> Logs { get; init; }

    /// <summary>
    /// Exception that caused failure. <see langword="null"/> when <see cref="Succeeded"/> is <see langword="true"/>.
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// UTC timestamp for the next retry attempt.
    /// <see langword="null"/> when the job has no remaining attempts (dead-letter) or succeeded.
    /// </summary>
    public DateTimeOffset? RetryAt { get; init; }

    /// <summary>
    /// Identifier of the recurring job that spawned this instance.
    /// <see langword="null"/> for manually enqueued jobs.
    /// </summary>
    public string? RecurringJobId { get; init; }
}
