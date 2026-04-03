namespace NexJob;

/// <summary>
/// Persisted definition of a recurring job. The scheduler uses this record to
/// calculate the next execution time and enqueue a <see cref="JobRecord"/> on schedule.
/// </summary>
/// <remarks>
/// This record defines the schedule — it is NOT a job execution.
/// Each time the cron fires, a new <see cref="JobRecord"/> is created and enqueued.
/// Modifying this record via <see cref="IScheduler.RecurringAsync{TJob,TInput}"/>
/// updates the schedule going forward; it does not affect already-enqueued instances.
/// </remarks>
public sealed class RecurringJobRecord
{
    /// <summary>User-defined identifier for this recurring job. Must be unique.</summary>
    public string RecurringJobId { get; init; } = string.Empty;

    /// <summary>Assembly-qualified type name of the <see cref="IJob{TInput}"/> implementation.</summary>
    /// <remarks>Internal storage field. Use the <c>Job</c> name in appsettings.json — NexJob resolves the type automatically.</remarks>
    public string JobType { get; init; } = string.Empty;

    /// <summary>Assembly-qualified type name of the job's input parameter type.</summary>
    /// <remarks>Internal storage field. Use the <c>Job</c> name in appsettings.json — NexJob resolves the type automatically.</remarks>
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

    /// <summary>
    /// User-supplied cron override. When set, the scheduler uses this expression instead
    /// of <see cref="Cron"/>. Set to <see langword="null"/> to revert to the default cron.
    /// </summary>
    public string? CronOverride { get; set; }

    /// <summary>
    /// When <see langword="false"/> the scheduler skips this job at every firing.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/> this job was soft-deleted by the user via
    /// <see cref="Storage.IStorageProvider.ForceDeleteRecurringJobAsync"/>. The scheduler skips
    /// it at every firing and it will not be resurrected by a subsequent call to
    /// <see cref="Storage.IStorageProvider.UpsertRecurringJobAsync"/>.
    /// Use <see cref="Storage.IStorageProvider.RestoreRecurringJobAsync"/> to reverse the deletion.
    /// </summary>
    public bool DeletedByUser { get; set; }
}
