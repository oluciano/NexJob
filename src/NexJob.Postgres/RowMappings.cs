namespace NexJob.Postgres;

/// <summary>Dapper row mapping for nexjob_jobs.</summary>
internal sealed class JobRow
{
    public Guid Id { get; set; }
    public string JobType { get; set; } = string.Empty;
    public string InputType { get; set; } = string.Empty;
    public string InputJson { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public string Queue { get; set; } = "default";
    public int Priority { get; set; } = 3;
    public string Status { get; set; } = "Enqueued";
    public string? IdempotencyKey { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 10;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? ProcessingStartedAt { get; set; }
    public DateTimeOffset? HeartbeatAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? RetryAt { get; set; }
    public string? ExceptionMessage { get; set; }
    public string? ExceptionStackTrace { get; set; }
    public Guid? ParentJobId { get; set; }

    public JobRecord ToRecord() => new()
    {
        Id                  = new JobId(Id),
        JobType             = JobType,
        InputType           = InputType,
        InputJson           = InputJson,
        SchemaVersion       = SchemaVersion,
        Queue               = Queue,
        Priority            = (JobPriority)Priority,
        Status              = Enum.Parse<JobStatus>(Status),
        IdempotencyKey      = IdempotencyKey,
        Attempts            = Attempts,
        MaxAttempts         = MaxAttempts,
        CreatedAt           = CreatedAt,
        ScheduledAt         = ScheduledAt,
        ProcessingStartedAt = ProcessingStartedAt,
        HeartbeatAt         = HeartbeatAt,
        CompletedAt         = CompletedAt,
        RetryAt             = RetryAt,
        LastErrorMessage    = ExceptionMessage,
        LastErrorStackTrace = ExceptionStackTrace,
        ParentJobId         = ParentJobId.HasValue ? new JobId(ParentJobId.Value) : null,
    };
}

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

    public RecurringJobRecord ToRecord() => new()
    {
        RecurringJobId = RecurringJobId,
        JobType        = JobType,
        InputType      = InputType,
        InputJson      = InputJson,
        Cron           = Cron,
        TimeZoneId     = TimeZoneId == "UTC" ? null : TimeZoneId,
        Queue          = Queue,
        NextExecution  = NextExecution,
        CreatedAt      = CreatedAt,
        LastExecutedAt = LastExecution,
    };
}
