using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NexJob.MongoDB;

/// <summary>BSON document that maps to a <see cref="RecurringJobRecord"/>.</summary>
internal sealed class RecurringJobDocument
{
    [BsonId]
    public string RecurringJobId { get; set; } = string.Empty;
    public string JobType { get; set; } = string.Empty;
    public string InputType { get; set; } = string.Empty;
    public string InputJson { get; set; } = string.Empty;
    public string Cron { get; set; } = string.Empty;
    public string? TimeZoneId { get; set; }
    public string Queue { get; set; } = "default";
    public DateTimeOffset? NextExecution { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastExecutedAt { get; set; }
    [BsonRepresentation(BsonType.String)]
    public JobStatus? LastExecutionStatus { get; set; }
    public string? LastExecutionError { get; set; }
    [BsonRepresentation(BsonType.String)]
    public RecurringConcurrencyPolicy ConcurrencyPolicy { get; set; } = RecurringConcurrencyPolicy.SkipIfRunning;

    /// <summary>User-supplied cron override; <see langword="null"/> means use <see cref="Cron"/>.</summary>
    public string? CronOverride { get; set; }

    /// <summary>When <see langword="false"/> the scheduler skips this job. Defaults to <see langword="true"/>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/> this job was soft-deleted by the user via
    /// <see cref="NexJob.Storage.IRecurringStorage.ForceDeleteRecurringJobAsync"/>. Defaults to <see langword="false"/>.
    /// </summary>
    [BsonElement("deleted_by_user")]
    public bool DeletedByUser { get; set; }

    public static RecurringJobDocument FromRecord(RecurringJobRecord r) => new()
    {
        RecurringJobId = r.RecurringJobId,
        JobType = r.JobType,
        InputType = r.InputType,
        InputJson = r.InputJson,
        Cron = r.Cron,
        TimeZoneId = r.TimeZoneId,
        Queue = r.Queue,
        NextExecution = r.NextExecution,
        CreatedAt = r.CreatedAt,
        LastExecutedAt = r.LastExecutedAt,
        LastExecutionStatus = r.LastExecutionStatus,
        LastExecutionError = r.LastExecutionError,
        ConcurrencyPolicy = r.ConcurrencyPolicy,
        CronOverride = r.CronOverride,
        Enabled = r.Enabled,
        DeletedByUser = r.DeletedByUser,
    };

    public RecurringJobRecord ToRecord() => new()
    {
        RecurringJobId = RecurringJobId,
        JobType = JobType,
        InputType = InputType,
        InputJson = InputJson,
        Cron = Cron,
        TimeZoneId = TimeZoneId,
        Queue = Queue,
        NextExecution = NextExecution,
        CreatedAt = CreatedAt,
        LastExecutedAt = LastExecutedAt,
        LastExecutionStatus = LastExecutionStatus,
        LastExecutionError = LastExecutionError,
        ConcurrencyPolicy = ConcurrencyPolicy,
        CronOverride = CronOverride,
        Enabled = Enabled,
        DeletedByUser = DeletedByUser,
    };
}
