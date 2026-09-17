namespace NexJob.Trigger.SalesforceStreaming;

/// <summary>
/// Specifies the starting replay position when subscribing to a Salesforce Streaming API channel.
/// </summary>
public enum SalesforceStreamingReplayPreset
{
    /// <summary>
    /// Starts receiving events published after the subscription is created (Replay ID = -1).
    /// </summary>
    Latest = -1,

    /// <summary>
    /// Starts receiving all retained events within the 24-hour retention window (Replay ID = -2).
    /// </summary>
    Earliest = -2,

    /// <summary>
    /// Starts receiving events from a custom Replay ID or previously persisted Replay ID.
    /// </summary>
    Custom = 0,
}
