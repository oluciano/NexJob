namespace NexJob;

/// <summary>A single captured log entry from a job execution.</summary>
public sealed class JobExecutionLog
{
    /// <summary>UTC timestamp when the log entry was emitted.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Log level (e.g. Information, Warning, Error).</summary>
    public string Level { get; init; } = string.Empty;

    /// <summary>Formatted log message.</summary>
    public string Message { get; init; } = string.Empty;
}
