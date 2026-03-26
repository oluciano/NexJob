namespace NexJob.MongoDB;

/// <summary>BSON-friendly representation of a <see cref="JobExecutionLog"/> entry stored in MongoDB.</summary>
internal sealed class ExecutionLogEntry
{
    /// <summary>UTC timestamp when the log entry was emitted.</summary>
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>Log level string (e.g. Information, Warning, Error).</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>Formatted log message.</summary>
    public string Message { get; set; } = string.Empty;
}
