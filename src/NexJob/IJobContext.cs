namespace NexJob;

/// <summary>
/// Provides runtime information about the currently executing job.
/// Inject via constructor — NexJob registers a scoped instance per job execution.
/// Zero changes to <see cref="IJob{TInput}"/> required.
/// </summary>
public interface IJobContext
{
    /// <summary>The unique identifier of the currently executing job.</summary>
    JobId JobId { get; }

    /// <summary>Current attempt number, 1-based. First execution = 1.</summary>
    int Attempt { get; }

    /// <summary>Maximum number of attempts configured for this job.</summary>
    int MaxAttempts { get; }

    /// <summary>Name of the queue this job was fetched from.</summary>
    string Queue { get; }

    /// <summary>
    /// Recurring job definition ID, or <see langword="null"/> when the job
    /// was enqueued directly (not by the recurring scheduler).
    /// </summary>
    string? RecurringJobId { get; }

    /// <summary>Tags attached to this job at enqueue time.</summary>
    IReadOnlyList<string> Tags { get; }

    /// <summary>
    /// Reports execution progress to the storage layer.
    /// The dashboard reflects updates in real time via SSE.
    /// </summary>
    /// <param name="percent">Progress percentage, 0–100.</param>
    /// <param name="message">Optional human-readable status message.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ReportProgressAsync(int percent, string? message = null, CancellationToken ct = default);
}
