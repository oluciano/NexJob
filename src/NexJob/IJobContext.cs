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

    /// <summary>
    /// Deserializes and returns the last saved checkpoint state for this job instance,
    /// or <see langword="null"/> if no checkpoint has been persisted yet (e.g. on first attempt).
    /// </summary>
    /// <typeparam name="TState">The checkpoint state model type.</typeparam>
    /// <returns>The deserialized state object, or <see langword="null"/>.</returns>
    TState? GetCheckpoint<TState>()
        where TState : class;

    /// <summary>
    /// Persists an execution checkpoint and updates visual progress in a single atomic database operation.
    /// Enables long-running batch jobs to resume exactly where they left off if interrupted by server restarts or pod scaling.
    /// </summary>
    /// <typeparam name="TState">The checkpoint state model type.</typeparam>
    /// <param name="state">The checkpoint state to serialize and persist.</param>
    /// <param name="percent">Optional progress percentage (0–100) to update on the dashboard.</param>
    /// <param name="message">Optional human-readable progress status message.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveCheckpointAsync<TState>(
        TState state,
        int? percent = null,
        string? message = null,
        CancellationToken ct = default)
        where TState : class;
}
