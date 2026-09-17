namespace NexJob.Trigger.Salesforce;

/// <summary>
/// Policy governing fallback behaviour when an expired or invalid Replay ID is encountered.
/// </summary>
public enum ReplayFallbackPolicy
{
    /// <summary>
    /// Fail-fast: logs a critical diagnostic error and stops the trigger service to prevent silent data loss.
    /// </summary>
    FailFast = 0,

    /// <summary>
    /// Discards the stale offset and resumes listening for future events from the tip of the stream.
    /// </summary>
    ResetToLatest = 1,

    /// <summary>
    /// Discards the stale offset and falls back to the earliest events still retained in Salesforce.
    /// </summary>
    ResetToEarliest = 2,
}
