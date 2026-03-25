namespace NexJob;

/// <summary>Real-time metrics for a single queue.</summary>
public sealed class QueueMetrics
{
    /// <summary>Queue name.</summary>
    public string Queue { get; init; } = string.Empty;

    /// <summary>Number of jobs waiting to be claimed.</summary>
    public int Enqueued { get; init; }

    /// <summary>Number of jobs currently being processed.</summary>
    public int Processing { get; init; }
}
