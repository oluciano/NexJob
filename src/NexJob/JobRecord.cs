namespace NexJob;

/// <summary>
/// Persisted representation of a single job execution. Stored and retrieved by
/// <see cref="NexJob.Storage.IStorageProvider"/>.
/// </summary>
public sealed class JobRecord
{
    /// <summary>Unique identifier for this job instance.</summary>
    public JobId Id { get; init; }

    /// <summary>
    /// Assembly-qualified type name of the <see cref="IJob{TInput}"/> implementation.
    /// Used to resolve the job class from the DI container at execution time.
    /// </summary>
    public string JobType { get; init; } = string.Empty;

    /// <summary>
    /// Assembly-qualified type name of the job's input parameter type.
    /// Used to deserialize <see cref="InputJson"/> at execution time.
    /// </summary>
    public string InputType { get; init; } = string.Empty;

    /// <summary>JSON-serialized job input payload.</summary>
    public string InputJson { get; init; } = string.Empty;

    /// <summary>
    /// Schema version of the input payload. Used with <see cref="IJobMigration{TOld,TNew}"/>
    /// to migrate payloads across breaking input changes.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Name of the queue this job belongs to. Defaults to <c>default</c>.</summary>
    public string Queue { get; init; } = "default";

    /// <summary>Execution priority. Defaults to <see cref="JobPriority.Normal"/>.</summary>
    public JobPriority Priority { get; init; } = JobPriority.Normal;

    /// <summary>Current lifecycle state of the job.</summary>
    public JobStatus Status { get; set; } = JobStatus.Enqueued;

    /// <summary>
    /// Optional idempotency key. If a job with this key already exists in
    /// <see cref="JobStatus.Enqueued"/> or <see cref="JobStatus.Processing"/> state,
    /// a duplicate enqueue is silently skipped and the existing <see cref="JobId"/> is returned.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Number of execution attempts made so far.</summary>
    public int Attempts { get; set; }

    /// <summary>Maximum number of attempts before the job is moved to the dead-letter state.</summary>
    public int MaxAttempts { get; init; } = 10;

    /// <summary>UTC timestamp when the job was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// UTC timestamp when the job should become eligible for execution.
    /// <see langword="null"/> for immediately-enqueued jobs.
    /// </summary>
    public DateTimeOffset? ScheduledAt { get; init; }

    /// <summary>
    /// UTC deadline for this job. If the job has not started executing by this time,
    /// it is marked as <see cref="JobStatus.Expired"/> and skipped.
    /// <see langword="null"/> means no deadline.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>UTC timestamp when a worker last claimed this job.</summary>
    public DateTimeOffset? ProcessingStartedAt { get; set; }

    /// <summary>
    /// UTC timestamp of the last heartbeat update. Used by <see cref="NexJob.Internal.OrphanedJobWatcherService"/>
    /// to detect and requeue stalled jobs.
    /// </summary>
    public DateTimeOffset? HeartbeatAt { get; set; }

    /// <summary>UTC timestamp when the job finished (succeeded or permanently failed).</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// UTC timestamp when the job should next be retried after a transient failure.
    /// <see langword="null"/> when the job has not failed or has no remaining attempts.
    /// </summary>
    public DateTimeOffset? RetryAt { get; set; }

    /// <summary>Error message from the last failed attempt.</summary>
    public string? LastErrorMessage { get; set; }

    /// <summary>Stack trace from the last failed attempt.</summary>
    public string? LastErrorStackTrace { get; set; }

    /// <summary>
    /// ID of the parent job that must complete before this job is enqueued.
    /// Set when the job is created via <see cref="IScheduler.ContinueWithAsync{TJob,TInput}"/>.
    /// </summary>
    public JobId? ParentJobId { get; init; }

    /// <summary>
    /// Identifier of the <see cref="RecurringJobRecord"/> that spawned this job instance,
    /// or <see langword="null"/> for manually enqueued jobs.
    /// Used to update <see cref="RecurringJobRecord.LastExecutionStatus"/> on completion.
    /// </summary>
    public string? RecurringJobId { get; init; }

    /// <summary>
    /// W3C traceparent header value captured at enqueue time.
    /// Used by <see cref="NexJob.Telemetry.NexJobActivitySource"/> to restore the distributed
    /// trace context when the job is executed, linking execution spans back to the enqueue span.
    /// </summary>
    public string? TraceParent { get; init; }

    /// <summary>Log entries captured during the last execution of this job.</summary>
    public IReadOnlyList<JobExecutionLog> ExecutionLogs { get; set; } = Array.Empty<JobExecutionLog>();

    /// <summary>
    /// Tags attached at enqueue time. Used for dashboard filtering and
    /// programmatic lookup via <see cref="NexJob.Storage.IStorageProvider.GetJobsByTagAsync"/>.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Current execution progress percentage, 0–100. <see langword="null"/> when not reported.</summary>
    public int? ProgressPercent { get; set; }

    /// <summary>Last progress message reported by the job. <see langword="null"/> when not reported.</summary>
    public string? ProgressMessage { get; set; }
}
