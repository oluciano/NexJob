namespace NexJob;

/// <summary>
/// Represents an active NexJob worker node/server.
/// </summary>
public class ServerRecord
{
    /// <summary>
    /// A unique identifier for the server (typically MachineName + Guid).
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// The number of concurrent workers configured for this server.
    /// </summary>
    public int WorkerCount { get; init; }

    /// <summary>
    /// The queues this server is actively polling.
    /// </summary>
    public IReadOnlyList<string> Queues { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The UTC timestamp when this server was started.
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// The UTC timestamp of the last successful heartbeat.
    /// Used to determine if the server has crashed or is offline.
    /// </summary>
    public DateTimeOffset HeartbeatAt { get; set; }
}
