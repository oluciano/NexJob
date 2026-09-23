namespace NexJob;

/// <summary>
/// A point-in-time snapshot of an event listener's state and configuration.
/// </summary>
/// <param name="Id">A unique identifier for this listener instance.</param>
/// <param name="Broker">The messaging broker or system name.</param>
/// <param name="Endpoint">The queue, topic, or subscription being consumed.</param>
/// <param name="TargetJobType">The name of the job type that messages are mapped to.</param>
/// <param name="Status">The current connection and operational status.</param>
/// <param name="StatusDescription">Diagnostic description or error details, if any.</param>
/// <param name="JobTag">Optional tag attached to enqueued jobs for filtering.</param>
/// <param name="ConsumerGroup">Optional consumer group or subscription identifier.</param>
/// <param name="StartedAt">UTC timestamp when the listener was registered.</param>
/// <param name="StatusChangedAt">UTC timestamp of the last status transition.</param>
public sealed record ListenerSnapshot(
    string Id,
    string Broker,
    string Endpoint,
    string TargetJobType,
    ListenerStatus Status,
    string? StatusDescription,
    string? JobTag,
    string? ConsumerGroup,
    DateTimeOffset StartedAt,
    DateTimeOffset StatusChangedAt);
