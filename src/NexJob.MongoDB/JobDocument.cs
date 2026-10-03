using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NexJob.MongoDB;

/// <summary>BSON document that maps to a <see cref="JobRecord"/>.</summary>
[BsonIgnoreExtraElements] // a node must read documents written by a newer version (rolling upgrades)
internal sealed class JobDocument
{
    [BsonId]
    public JobId Id { get; set; }

    public string JobType { get; set; } = string.Empty;
    public string InputType { get; set; } = string.Empty;
    public string InputJson { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public string Queue { get; set; } = "default";
    [BsonRepresentation(BsonType.Int32)]   // numeric sort: Critical=1 … Low=4
    public JobPriority Priority { get; set; } = JobPriority.Normal;
    [BsonRepresentation(BsonType.String)]  // human-readable in DB
    public JobStatus Status { get; set; } = JobStatus.Enqueued;
    public string? IdempotencyKey { get; set; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; set; } = 10;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public DateTimeOffset? ProcessingStartedAt { get; set; }
    public DateTimeOffset? HeartbeatAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? RetryAt { get; set; }
    public string? LastErrorMessage { get; set; }
    public string? LastErrorStackTrace { get; set; }
    public JobId? ParentJobId { get; set; }
    public string? RecurringJobId { get; set; }

    [BsonElement("execution_logs")]
    public List<ExecutionLogEntry>? ExecutionLogs { get; set; }

    public List<string> Tags { get; set; } = [];
    public int? ProgressPercent { get; set; }
    public string? ProgressMessage { get; set; }
    public string? CheckpointJson { get; set; }
    [BsonIgnoreIfNull] // not written without a deadline, so a node of the previous version can still read the job
    public DateTimeOffset? ExpiresAt { get; set; }

    // Every DateTimeOffset is stored at +00:00: the string serializer sorts and compares lexically, which is only
    // correct when all values share one offset.
    public static JobDocument FromRecord(JobRecord r) => new()
    {
        Id = r.Id,
        JobType = r.JobType,
        InputType = r.InputType,
        InputJson = r.InputJson,
        SchemaVersion = r.SchemaVersion,
        Queue = r.Queue,
        Priority = r.Priority,
        Status = r.Status,
        IdempotencyKey = r.IdempotencyKey,
        Attempts = r.Attempts,
        MaxAttempts = r.MaxAttempts,
        CreatedAt = r.CreatedAt.ToUniversalTime(),
        ScheduledAt = r.ScheduledAt?.ToUniversalTime(),
        ProcessingStartedAt = r.ProcessingStartedAt?.ToUniversalTime(),
        HeartbeatAt = r.HeartbeatAt?.ToUniversalTime(),
        CompletedAt = r.CompletedAt?.ToUniversalTime(),
        RetryAt = r.RetryAt?.ToUniversalTime(),
        LastErrorMessage = r.LastErrorMessage,
        LastErrorStackTrace = r.LastErrorStackTrace,
        ParentJobId = r.ParentJobId,
        RecurringJobId = r.RecurringJobId,
        ExecutionLogs = r.ExecutionLogs.Count == 0
            ? null
            : r.ExecutionLogs.Select(e => new ExecutionLogEntry
            {
                Timestamp = e.Timestamp.ToUniversalTime(),
                Level = e.Level,
                Message = e.Message,
            }).ToList(),
        Tags = r.Tags.ToList(),
        ProgressPercent = r.ProgressPercent,
        ProgressMessage = r.ProgressMessage,
        CheckpointJson = r.CheckpointJson,
        ExpiresAt = r.ExpiresAt?.ToUniversalTime(),
    };

    public JobRecord ToRecord() => new()
    {
        Id = Id,
        JobType = JobType,
        InputType = InputType,
        InputJson = InputJson,
        SchemaVersion = SchemaVersion,
        Queue = Queue,
        Priority = Priority,
        Status = Status,
        IdempotencyKey = IdempotencyKey,
        Attempts = Attempts,
        MaxAttempts = MaxAttempts,
        CreatedAt = CreatedAt,
        ScheduledAt = ScheduledAt,
        ProcessingStartedAt = ProcessingStartedAt,
        HeartbeatAt = HeartbeatAt,
        CompletedAt = CompletedAt,
        RetryAt = RetryAt,
        LastErrorMessage = LastErrorMessage,
        LastErrorStackTrace = LastErrorStackTrace,
        ParentJobId = ParentJobId,
        RecurringJobId = RecurringJobId,
        ExecutionLogs = ExecutionLogs is null
            ? Array.Empty<JobExecutionLog>()
            : ExecutionLogs.Select(e => new JobExecutionLog
            {
                Timestamp = e.Timestamp,
                Level = e.Level,
                Message = e.Message,
            }).ToList(),
        Tags = Tags,
        ProgressPercent = ProgressPercent,
        ProgressMessage = ProgressMessage,
        CheckpointJson = CheckpointJson,
        ExpiresAt = ExpiresAt,
    };
}
