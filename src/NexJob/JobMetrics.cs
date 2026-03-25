namespace NexJob;

/// <summary>Aggregated metrics snapshot for the NexJob dashboard overview.</summary>
public sealed class JobMetrics
{
    /// <summary>Jobs waiting to be picked up.</summary>
    public int Enqueued { get; init; }

    /// <summary>Jobs currently being executed by a worker.</summary>
    public int Processing { get; init; }

    /// <summary>Jobs that completed successfully.</summary>
    public int Succeeded { get; init; }

    /// <summary>Jobs that exhausted all retry attempts (dead-letter).</summary>
    public int Failed { get; init; }

    /// <summary>Jobs scheduled to run at a future time.</summary>
    public int Scheduled { get; init; }

    /// <summary>Number of registered recurring job definitions.</summary>
    public int Recurring { get; init; }

    /// <summary>Per-hour throughput for the last 24 hours (succeeded + failed).</summary>
    public IReadOnlyList<HourlyThroughput> HourlyThroughput { get; init; } = [];

    /// <summary>The 10 most recently failed jobs.</summary>
    public IReadOnlyList<JobRecord> RecentFailures { get; init; } = [];
}
