namespace NexJob.Postgres;

/// <summary>Dapper row mapping for nexjob_recurring_jobs.</summary>
internal sealed class RecurringJobRow
{
    public string RecurringJobId { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public string InputType { get; set; } = string.Empty;
    public string InputJson { get; set; } = string.Empty;
    public string Cron { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "UTC";
    public string Queue { get; set; } = "default";
    public DateTimeOffset? NextExecution { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastExecution { get; set; }
    public string? LastExecutionStatus { get; set; }
    public string? LastExecutionError { get; set; }
    public string ConcurrencyPolicy { get; set; } = "SkipIfRunning";
    public string? CronOverride { get; set; }
    public bool Enabled { get; set; } = true;
    public bool DeletedByUser { get; set; }

    public RecurringJobRecord ToRecord() => new()
    {
        RecurringJobId = RecurringJobId,
        JobType = JobType,
        InputType = InputType,
        InputJson = InputJson,
        Cron = Cron,
        TimeZoneId = TimeZoneId == "UTC" ? null : TimeZoneId,
        Queue = Queue,
        NextExecution = NextExecution,
        CreatedAt = CreatedAt,
        LastExecutedAt = LastExecution,
        LastExecutionStatus = LastExecutionStatus is not null
            ? Enum.Parse<JobStatus>(LastExecutionStatus) : null,
        LastExecutionError = LastExecutionError,
        ConcurrencyPolicy = Enum.Parse<RecurringConcurrencyPolicy>(ConcurrencyPolicy),
        CronOverride = CronOverride,
        Enabled = Enabled,
        DeletedByUser = DeletedByUser,
    };
}
