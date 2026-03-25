namespace NexJob;

/// <summary>
/// Strongly-typed identifier for a job. Wraps a <see cref="Guid"/> to prevent accidental
/// mixing with other identifiers.
/// </summary>
public readonly record struct JobId(Guid Value)
{
    /// <summary>Creates a new <see cref="JobId"/> with a fresh random GUID.</summary>
    public static JobId New() => new(Guid.NewGuid());

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Priority levels for job execution. Lower numeric values are processed first.
/// </summary>
public enum JobPriority
{
    /// <summary>Processed before all other levels. Reserved for time-critical operations.</summary>
    Critical = 1,

    /// <summary>Processed after <see cref="Critical"/> jobs.</summary>
    High = 2,

    /// <summary>Default priority for most background work.</summary>
    Normal = 3,

    /// <summary>Processed only when no higher-priority jobs are waiting.</summary>
    Low = 4,
}

/// <summary>
/// Lifecycle states of a persisted job.
/// </summary>
public enum JobStatus
{
    /// <summary>The job is waiting to be picked up by a worker.</summary>
    Enqueued,

    /// <summary>The job is scheduled to run at a future point in time.</summary>
    Scheduled,

    /// <summary>A worker has claimed the job and is currently executing it.</summary>
    Processing,

    /// <summary>The job completed successfully.</summary>
    Succeeded,

    /// <summary>The job exhausted all retry attempts and moved to the dead-letter state.</summary>
    Failed,

    /// <summary>The job was explicitly deleted before execution.</summary>
    Deleted,

    /// <summary>The job is waiting for its parent job to complete.</summary>
    AwaitingContinuation,
}

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
}

/// <summary>
/// Controls how the recurring job scheduler behaves when a previous instance of the
/// same job is still <see cref="JobStatus.Enqueued"/> or <see cref="JobStatus.Processing"/>.
/// </summary>
public enum RecurringConcurrencyPolicy
{
    /// <summary>
    /// Default. If an instance of this job is already queued or running, the new
    /// firing is silently skipped. Safe for jobs that must not overlap (reports,
    /// clean-up tasks, anything that assumes exclusive access to a resource).
    /// </summary>
    SkipIfRunning = 0,

    /// <summary>
    /// Every cron firing (and every manual trigger) creates a new job instance
    /// regardless of how many are already running. Use this when concurrent
    /// instances are safe and desirable — for example, range-based data processing
    /// where each instance locks its own shard and works independently.
    /// </summary>
    AllowConcurrent = 1,
}

/// <summary>
/// Persisted definition of a recurring job. The scheduler uses this record to
/// calculate the next execution time and enqueue a <see cref="JobRecord"/> on schedule.
/// </summary>
public sealed class RecurringJobRecord
{
    /// <summary>User-defined identifier for this recurring job. Must be unique.</summary>
    public string RecurringJobId { get; init; } = string.Empty;

    /// <summary>Assembly-qualified type name of the <see cref="IJob{TInput}"/> implementation.</summary>
    public string JobType { get; init; } = string.Empty;

    /// <summary>Assembly-qualified type name of the job's input parameter type.</summary>
    public string InputType { get; init; } = string.Empty;

    /// <summary>JSON-serialized job input payload.</summary>
    public string InputJson { get; init; } = string.Empty;

    /// <summary>Cron expression that defines the execution schedule.</summary>
    public string Cron { get; init; } = string.Empty;

    /// <summary>
    /// IANA or Windows time-zone ID used when evaluating the cron expression.
    /// <see langword="null"/> defaults to UTC.
    /// </summary>
    public string? TimeZoneId { get; init; }

    /// <summary>Name of the queue where the recurring job's instances will be enqueued.</summary>
    public string Queue { get; init; } = "default";

    /// <summary>UTC timestamp of the next scheduled execution.</summary>
    public DateTimeOffset? NextExecution { get; set; }

    /// <summary>UTC timestamp when this recurring job definition was first created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>UTC timestamp of the most recent execution, or <see langword="null"/> if never run.</summary>
    public DateTimeOffset? LastExecutedAt { get; set; }

    /// <summary>
    /// Status of the most recent execution (<see cref="JobStatus.Succeeded"/> or
    /// <see cref="JobStatus.Failed"/>), or <see langword="null"/> if never run.
    /// </summary>
    public JobStatus? LastExecutionStatus { get; set; }

    /// <summary>
    /// Error message from the most recent failed execution, or <see langword="null"/> if the
    /// last execution succeeded or the job has never run.
    /// </summary>
    public string? LastExecutionError { get; set; }

    /// <summary>
    /// Controls what happens when a new firing occurs while a previous instance is still
    /// running. Defaults to <see cref="RecurringConcurrencyPolicy.SkipIfRunning"/>.
    /// </summary>
    public RecurringConcurrencyPolicy ConcurrencyPolicy { get; init; } =
        RecurringConcurrencyPolicy.SkipIfRunning;
}
