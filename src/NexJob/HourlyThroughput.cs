namespace NexJob;

/// <summary>Number of completed jobs (succeeded + failed) in a single hour window.</summary>
public sealed class HourlyThroughput
{
    /// <summary>The start of the one-hour bucket (UTC, truncated to the hour).</summary>
    public DateTimeOffset Hour { get; init; }

    /// <summary>Number of jobs that completed within this hour.</summary>
    public int Count { get; init; }
}
