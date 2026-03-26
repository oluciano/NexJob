namespace NexJob.Configuration;

/// <summary>Per-queue configuration entry in <c>appsettings.json</c>.</summary>
public sealed class QueueSettings
{
    /// <summary>Queue name. Required.</summary>
    public required string Name { get; set; }

    /// <summary>Worker count override for this queue. When <see langword="null"/>, the global value is used.</summary>
    public int? Workers { get; set; }

    /// <summary>Optional time window during which this queue is processed.</summary>
    public ExecutionWindowSettings? ExecutionWindow { get; set; }
}
