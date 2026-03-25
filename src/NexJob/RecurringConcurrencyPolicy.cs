namespace NexJob;

/// <summary>
/// Controls how the recurring job scheduler behaves when a previous instance of the
/// same job is still <see cref="JobStatus.Enqueued"/> or <see cref="JobStatus.Processing"/>.
/// </summary>
public enum RecurringConcurrencyPolicy
{
    /// <summary>
    /// Default. If an instance of this job is already queued or running, the new
    /// firing is silently skipped. Safe for jobs that must not overlap (reports,
    /// clean-up tasks, anything that assumes exclusive access to a resource).
    /// </summary>
    SkipIfRunning = 0,

    /// <summary>
    /// Every cron firing (and every manual trigger) creates a new job instance
    /// regardless of how many are already running. Use this when concurrent
    /// instances are safe and desirable — for example, range-based data processing
    /// where each instance locks its own shard and works independently.
    /// </summary>
    AllowConcurrent = 1,
}
