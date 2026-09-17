namespace NexJob.Trigger.Salesforce;

/// <summary>
/// Supported subscription replay presets for Salesforce Pub/Sub API.
/// </summary>
public enum SalesforceReplayPreset
{
    /// <summary>
    /// Start subscription at the tip of the stream (new events only).
    /// </summary>
    Latest = 0,

    /// <summary>
    /// Start subscription at the earliest point within the retention window (typically 24 hours).
    /// </summary>
    Earliest = 1,

    /// <summary>
    /// Start subscription after a specific replay ID.
    /// </summary>
    Custom = 2,
}
