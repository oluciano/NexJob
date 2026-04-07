namespace NexJob;

/// <summary>
/// Entry point for scheduling background work. Inject this interface into your
/// application services to enqueue, schedule, and manage jobs.
/// </summary>
public interface IScheduler
{
    /// <summary>
    /// Immediately enqueues a no-input job for background execution.
    /// </summary>
    /// <typeparam name="TJob">The <see cref="IJob"/> implementation to execute.</typeparam>
    /// <param name="queue">
    /// Target queue name. Uses the default queue when <see langword="null"/>.
    /// </param>
    /// <param name="priority">Execution priority within the queue.</param>
    /// <param name="idempotencyKey">
    /// Optional deduplication key. If a job with this key already exists in
    /// <see cref="JobStatus.Enqueued"/> or <see cref="JobStatus.Processing"/> state,
    /// the existing <see cref="JobId"/> is returned and no new job is created.
    /// Idempotency only applies to active states (<see cref="JobStatus.Enqueued"/>
    /// and <see cref="JobStatus.Processing"/>). Jobs in terminal states
    /// (<see cref="JobStatus.Failed"/>, <see cref="JobStatus.Succeeded"/>,
    /// <see cref="JobStatus.Expired"/>) are considered complete — a new job
    /// will be created if the same key is enqueued again.
    /// If your job has side effects, ensure it is idempotent before re-enqueuing.
    /// </param>
    /// <param name="duplicatePolicy">
    /// Controls the behaviour when a job with the same <paramref name="idempotencyKey"/>
    /// already exists in a terminal failure state (<see cref="JobStatus.Failed"/>).
    /// Defaults to <see cref="DuplicatePolicy.AllowAfterFailed"/>.
    /// </param>
    /// <param name="tags">
    /// Optional list of searchable tags attached to the job.
    /// Tags can be used to filter jobs in the dashboard or via
    /// <see cref="GetJobsByTagAsync"/>.
    /// </param>
    /// <param name="deadlineAfter">
    /// Optional deadline for execution. If specified, the job must start executing
    /// within this duration from enqueue time, or it will be marked as <see cref="JobStatus.Expired"/>.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The identifier of the enqueued (or existing, if idempotent) job.</returns>
    Task<JobId> EnqueueAsync<TJob>(
        string? queue = null,
        JobPriority priority = JobPriority.Normal,
        string? idempotencyKey = null,
        DuplicatePolicy duplicatePolicy = DuplicatePolicy.AllowAfterFailed,
        IReadOnlyList<string>? tags = null,
        TimeSpan? deadlineAfter = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob;

    /// <summary>
    /// Immediately enqueues a job for background execution.
    /// </summary>
    /// <typeparam name="TJob">The <see cref="IJob{TInput}"/> implementation to execute.</typeparam>
    /// <typeparam name="TInput">The input type accepted by <typeparamref name="TJob"/>.</typeparam>
    /// <param name="input">The input value to pass to the job.</param>
    /// <param name="queue">
    /// Target queue name. Uses the default queue when <see langword="null"/>.
    /// </param>
    /// <param name="priority">Execution priority within the queue.</param>
    /// <param name="idempotencyKey">
    /// Optional deduplication key. If a job with this key already exists in
    /// <see cref="JobStatus.Enqueued"/> or <see cref="JobStatus.Processing"/> state,
    /// the existing <see cref="JobId"/> is returned and no new job is created.
    /// Idempotency only applies to active states (<see cref="JobStatus.Enqueued"/>
    /// and <see cref="JobStatus.Processing"/>). Jobs in terminal states
    /// (<see cref="JobStatus.Failed"/>, <see cref="JobStatus.Succeeded"/>,
    /// <see cref="JobStatus.Expired"/>) are considered complete — a new job
    /// will be created if the same key is enqueued again.
    /// If your job has side effects, ensure it is idempotent before re-enqueuing.
    /// </param>
    /// <param name="duplicatePolicy">
    /// Controls the behaviour when a job with the same <paramref name="idempotencyKey"/>
    /// already exists in a terminal failure state (<see cref="JobStatus.Failed"/>).
    /// Defaults to <see cref="DuplicatePolicy.AllowAfterFailed"/>.
    /// </param>
    /// <param name="tags">
    /// Optional list of searchable tags attached to the job.
    /// Tags can be used to filter jobs in the dashboard or via
    /// <see cref="GetJobsByTagAsync"/>.
    /// </param>
    /// <param name="deadlineAfter">
    /// Optional deadline for execution. If specified, the job must start executing
    /// within this duration from enqueue time, or it will be marked as <see cref="JobStatus.Expired"/>.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The identifier of the enqueued (or existing, if idempotent) job.</returns>
    Task<JobId> EnqueueAsync<TJob, TInput>(
        TInput input,
        string? queue = null,
        JobPriority priority = JobPriority.Normal,
        string? idempotencyKey = null,
        DuplicatePolicy duplicatePolicy = DuplicatePolicy.AllowAfterFailed,
        IReadOnlyList<string>? tags = null,
        TimeSpan? deadlineAfter = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput>;

    /// <summary>
    /// Schedules a no-input job to execute after the specified delay.
    /// </summary>
    /// <typeparam name="TJob">The <see cref="IJob"/> implementation to execute.</typeparam>
    /// <param name="delay">How long to wait before the job becomes eligible for execution.</param>
    /// <param name="queue">Target queue name. Uses the default queue when <see langword="null"/>.</param>
    /// <param name="idempotencyKey">Optional deduplication key.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The identifier of the scheduled job.</returns>
    Task<JobId> ScheduleAsync<TJob>(
        TimeSpan delay,
        string? queue = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob;

    /// <summary>
    /// Schedules a job to execute after the specified delay.
    /// </summary>
    /// <typeparam name="TJob">The <see cref="IJob{TInput}"/> implementation to execute.</typeparam>
    /// <typeparam name="TInput">The input type accepted by <typeparamref name="TJob"/>.</typeparam>
    /// <param name="input">The input value to pass to the job.</param>
    /// <param name="delay">How long to wait before the job becomes eligible for execution.</param>
    /// <param name="queue">Target queue name. Uses the default queue when <see langword="null"/>.</param>
    /// <param name="idempotencyKey">Optional deduplication key.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The identifier of the scheduled job.</returns>
    Task<JobId> ScheduleAsync<TJob, TInput>(
        TInput input,
        TimeSpan delay,
        string? queue = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput>;

    /// <summary>
    /// Schedules a no-input job to execute at a specific point in time.
    /// </summary>
    /// <typeparam name="TJob">The <see cref="IJob"/> implementation to execute.</typeparam>
    /// <param name="runAt">The UTC instant at which the job becomes eligible for execution.</param>
    /// <param name="queue">Target queue name. Uses the default queue when <see langword="null"/>.</param>
    /// <param name="idempotencyKey">Optional deduplication key.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The identifier of the scheduled job.</returns>
    Task<JobId> ScheduleAtAsync<TJob>(
        DateTimeOffset runAt,
        string? queue = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob;

    /// <summary>
    /// Schedules a job to execute at a specific point in time.
    /// </summary>
    /// <typeparam name="TJob">The <see cref="IJob{TInput}"/> implementation to execute.</typeparam>
    /// <typeparam name="TInput">The input type accepted by <typeparamref name="TJob"/>.</typeparam>
    /// <param name="input">The input value to pass to the job.</param>
    /// <param name="runAt">The UTC instant at which the job becomes eligible for execution.</param>
    /// <param name="queue">Target queue name. Uses the default queue when <see langword="null"/>.</param>
    /// <param name="idempotencyKey">Optional deduplication key.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The identifier of the scheduled job.</returns>
    Task<JobId> ScheduleAtAsync<TJob, TInput>(
        TInput input,
        DateTimeOffset runAt,
        string? queue = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput>;

    /// <summary>
    /// Creates or updates a recurring job that fires on a cron schedule.
    /// </summary>
    /// <typeparam name="TJob">The <see cref="IJob{TInput}"/> implementation to execute.</typeparam>
    /// <typeparam name="TInput">The input type accepted by <typeparamref name="TJob"/>.</typeparam>
    /// <param name="recurringJobId">
    /// Unique identifier for this recurring job definition. Calling this method again
    /// with the same ID updates the existing definition.
    /// </param>
    /// <param name="input">The input value to pass to each job instance.</param>
    /// <param name="cron">A valid cron expression (5-field or 6-field with seconds).</param>
    /// <param name="timeZone">
    /// Time zone used to evaluate the cron expression. Defaults to UTC.
    /// </param>
    /// <param name="queue">Target queue name. Uses the default queue when <see langword="null"/>.</param>
    /// <param name="concurrencyPolicy">
    /// Controls what happens when a new cron firing occurs while a previous instance is still
    /// running. <see cref="RecurringConcurrencyPolicy.SkipIfRunning"/> (default) silently skips
    /// the new firing; <see cref="RecurringConcurrencyPolicy.AllowConcurrent"/> always enqueues
    /// a new instance, enabling parallel execution of the same job.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task RecurringAsync<TJob, TInput>(
        string recurringJobId,
        TInput input,
        string cron,
        TimeZoneInfo? timeZone = null,
        string? queue = null,
        RecurringConcurrencyPolicy concurrencyPolicy = RecurringConcurrencyPolicy.SkipIfRunning,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput>;

    /// <summary>
    /// Creates or updates a recurring job that fires on a cron schedule.
    /// Use this overload for jobs implementing <see cref="IJob"/> (no input required).
    /// </summary>
    /// <typeparam name="TJob">The <see cref="IJob"/> implementation to execute.</typeparam>
    /// <param name="recurringJobId">
    /// Unique identifier for this recurring job definition. Calling this method again
    /// with the same ID updates the existing definition.
    /// </param>
    /// <param name="cron">A valid cron expression (5-field or 6-field with seconds).</param>
    /// <param name="timeZone">
    /// Time zone used to evaluate the cron expression. Defaults to UTC.
    /// </param>
    /// <param name="queue">Target queue name. Uses the default queue when <see langword="null"/>.</param>
    /// <param name="concurrencyPolicy">
    /// Controls what happens when a new cron firing occurs while a previous instance is still
    /// running. <see cref="RecurringConcurrencyPolicy.SkipIfRunning"/> (default) silently skips
    /// the new firing; <see cref="RecurringConcurrencyPolicy.AllowConcurrent"/> always enqueues
    /// a new instance, enabling parallel execution of the same job.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task RecurringAsync<TJob>(
        string recurringJobId,
        string cron,
        TimeZoneInfo? timeZone = null,
        string? queue = null,
        RecurringConcurrencyPolicy concurrencyPolicy = RecurringConcurrencyPolicy.SkipIfRunning,
        CancellationToken cancellationToken = default)
        where TJob : IJob;

    /// <summary>
    /// Schedules a job to execute immediately after the specified parent job succeeds.
    /// </summary>
    /// <typeparam name="TJob">The <see cref="IJob{TInput}"/> implementation to execute.</typeparam>
    /// <typeparam name="TInput">The input type accepted by <typeparamref name="TJob"/>.</typeparam>
    /// <param name="parentJobId">The identifier of the job that must complete before this one runs.</param>
    /// <param name="input">The input value to pass to the continuation job.</param>
    /// <param name="queue">Target queue name. Uses the default queue when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The identifier of the continuation job.</returns>
    Task<JobId> ContinueWithAsync<TJob, TInput>(
        JobId parentJobId,
        TInput input,
        string? queue = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput>;

    /// <summary>
    /// Schedules a no-input job to execute immediately after the specified parent job succeeds.
    /// </summary>
    /// <typeparam name="TJob">The <see cref="IJob"/> implementation to execute.</typeparam>
    /// <param name="parentJobId">The identifier of the job that must complete before this one runs.</param>
    /// <param name="queue">Target queue name. Uses the default queue when <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The identifier of the continuation job.</returns>
    Task<JobId> ContinueWithAsync<TJob>(
        JobId parentJobId,
        string? queue = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob;

    /// <summary>
    /// Removes a recurring job definition. Any already-enqueued instances are not affected.
    /// </summary>
    /// <param name="recurringJobId">The identifier of the recurring job to remove.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task RemoveRecurringAsync(
        string recurringJobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all jobs that have the specified tag attached.
    /// </summary>
    /// <param name="tag">The tag to search for.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task<IReadOnlyList<JobRecord>> GetJobsByTagAsync(
        string tag,
        CancellationToken cancellationToken = default);
}
