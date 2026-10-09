using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace NexJob.Internal;

/// <summary>
/// Builds <see cref="JobRecord"/> instances for enqueue operations.
/// Centralises record construction so that trigger packages and the scheduler
/// share identical job-building logic without duplication.
/// </summary>
internal static class JobRecordFactory
{
    /// <summary>
    /// Builds a <see cref="JobRecord"/> for a structured job with typed input.
    /// </summary>
    /// <typeparam name="TJob">The job type implementing <see cref="IJob{TInput}"/>.</typeparam>
    /// <typeparam name="TInput">The input type for the job.</typeparam>
    /// <param name="input">The serializable job input.</param>
    /// <param name="options">Global NexJob configuration.</param>
    /// <param name="queue">Target queue name; defaults to "default" if null.</param>
    /// <param name="priority">Execution priority; defaults to <see cref="JobPriority.Normal"/>.</param>
    /// <param name="idempotencyKey">Optional idempotency key for deduplication.</param>
    /// <param name="status">Job lifecycle status.</param>
    /// <param name="scheduledAt">UTC timestamp when the job becomes eligible for execution; null for immediate.</param>
    /// <param name="tags">Optional tags for dashboard filtering and lookup.</param>
    /// <param name="expiresAt">Optional UTC deadline; job is marked <see cref="JobStatus.Expired"/> if fetched after this time.</param>
    /// <param name="traceParent">Optional W3C traceparent header value; if null, captured from <see cref="Activity.Current"/> if available.</param>
    /// <param name="parentJobId">Optional parent job ID for continuation jobs.</param>
    /// <param name="maxAttempts">Explicit attempt limit for this job, or <see langword="null"/> to use the attribute or the global option.</param>
    /// <returns>A fully initialized <see cref="JobRecord"/> ready to persist.</returns>
    internal static JobRecord Build<TJob, TInput>(
        TInput input,
        NexJobOptions options,
        string? queue = null,
        JobPriority priority = JobPriority.Normal,
        string? idempotencyKey = null,
        JobStatus status = JobStatus.Enqueued,
        DateTimeOffset? scheduledAt = null,
        IReadOnlyList<string>? tags = null,
        DateTimeOffset? expiresAt = null,
        string? traceParent = null,
        JobId? parentJobId = null,
        int? maxAttempts = null)
        where TJob : IJob<TInput>
    {
        var capturedTraceParent = traceParent ?? Activity.Current?.Id;

        return new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(TJob).AssemblyQualifiedName!,
            InputType = typeof(TInput).AssemblyQualifiedName!,
            InputJson = JsonSerializer.Serialize(input),
            Queue = options.ResolveQueue(queue),
            Priority = priority,
            Status = status,
            IdempotencyKey = idempotencyKey,
            ScheduledAt = scheduledAt,
            ExpiresAt = expiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = ResolveMaxAttempts(typeof(TJob), maxAttempts, options),
            TraceParent = capturedTraceParent,
            Tags = tags ?? [],
            ParentJobId = parentJobId,
        };
    }

    /// <summary>
    /// Builds a <see cref="JobRecord"/> for a simple job without typed input.
    /// </summary>
    /// <typeparam name="TJob">The job type implementing <see cref="IJob"/>.</typeparam>
    /// <param name="options">Global NexJob configuration.</param>
    /// <param name="queue">Target queue name; defaults to "default" if null.</param>
    /// <param name="priority">Execution priority; defaults to <see cref="JobPriority.Normal"/>.</param>
    /// <param name="idempotencyKey">Optional idempotency key for deduplication.</param>
    /// <param name="status">Job lifecycle status.</param>
    /// <param name="scheduledAt">UTC timestamp when the job becomes eligible for execution; null for immediate.</param>
    /// <param name="tags">Optional tags for dashboard filtering and lookup.</param>
    /// <param name="expiresAt">Optional UTC deadline; job is marked <see cref="JobStatus.Expired"/> if fetched after this time.</param>
    /// <param name="traceParent">Optional W3C traceparent header value; if null, captured from <see cref="Activity.Current"/> if available.</param>
    /// <param name="parentJobId">Optional parent job ID for continuation jobs.</param>
    /// <param name="maxAttempts">Explicit attempt limit for this job, or <see langword="null"/> to use the attribute or the global option.</param>
    /// <returns>A fully initialized <see cref="JobRecord"/> ready to persist.</returns>
    internal static JobRecord Build<TJob>(
        NexJobOptions options,
        string? queue = null,
        JobPriority priority = JobPriority.Normal,
        string? idempotencyKey = null,
        JobStatus status = JobStatus.Enqueued,
        DateTimeOffset? scheduledAt = null,
        IReadOnlyList<string>? tags = null,
        DateTimeOffset? expiresAt = null,
        string? traceParent = null,
        JobId? parentJobId = null,
        int? maxAttempts = null)
        where TJob : IJob
    {
        var capturedTraceParent = traceParent ?? Activity.Current?.Id;

        return new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(TJob).AssemblyQualifiedName!,
            InputType = typeof(NoInput).AssemblyQualifiedName!,
            InputJson = JsonSerializer.Serialize(NoInput.Instance),
            Queue = options.ResolveQueue(queue),
            Priority = priority,
            Status = status,
            IdempotencyKey = idempotencyKey,
            ScheduledAt = scheduledAt,
            ExpiresAt = expiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = ResolveMaxAttempts(typeof(TJob), maxAttempts, options),
            TraceParent = capturedTraceParent,
            Tags = tags ?? [],
            ParentJobId = parentJobId,
        };
    }

    /// <summary>
    /// Builds a <see cref="JobRecord"/> for a job with runtime-determined type and JSON input.
    /// Used by external triggers that receive arbitrary job types from external sources.
    /// </summary>
    /// <param name="jobType">Assembly-qualified type name of the job implementation.</param>
    /// <param name="inputType">Assembly-qualified type name of the input type.</param>
    /// <param name="inputJson">JSON-serialized input payload.</param>
    /// <param name="options">Global NexJob configuration.</param>
    /// <param name="queue">Target queue name; defaults to "default" if null.</param>
    /// <param name="priority">Execution priority; defaults to <see cref="JobPriority.Normal"/>.</param>
    /// <param name="idempotencyKey">Optional idempotency key for deduplication.</param>
    /// <param name="status">Job lifecycle status.</param>
    /// <param name="scheduledAt">UTC timestamp when the job becomes eligible for execution; null for immediate.</param>
    /// <param name="tags">Optional tags for dashboard filtering and lookup.</param>
    /// <param name="expiresAt">Optional UTC deadline; job is marked <see cref="JobStatus.Expired"/> if fetched after this time.</param>
    /// <param name="traceParent">Optional W3C traceparent header value; if null, captured from <see cref="Activity.Current"/> if available.</param>
    /// <param name="parentJobId">Optional parent job ID for continuation jobs.</param>
    /// <param name="maxAttempts">Explicit attempt limit for this job, or <see langword="null"/> to use the attribute or the global option.</param>
    /// <returns>A fully initialized <see cref="JobRecord"/> ready to persist.</returns>
    internal static JobRecord Build(
        string jobType,
        string inputType,
        string inputJson,
        NexJobOptions options,
        string? queue = null,
        JobPriority priority = JobPriority.Normal,
        string? idempotencyKey = null,
        JobStatus status = JobStatus.Enqueued,
        DateTimeOffset? scheduledAt = null,
        IReadOnlyList<string>? tags = null,
        DateTimeOffset? expiresAt = null,
        string? traceParent = null,
        JobId? parentJobId = null,
        int? maxAttempts = null)
    {
        var capturedTraceParent = traceParent ?? Activity.Current?.Id;

        return new JobRecord
        {
            Id = JobId.New(),
            JobType = jobType,
            InputType = inputType,
            InputJson = inputJson,
            Queue = options.ResolveQueue(queue),
            Priority = priority,
            Status = status,
            IdempotencyKey = idempotencyKey,
            ScheduledAt = scheduledAt,
            ExpiresAt = expiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = ResolveMaxAttempts(JobTypeResolver.ResolveJobType(jobType), maxAttempts, options),
            TraceParent = capturedTraceParent,
            Tags = tags ?? [],
            ParentJobId = parentJobId,
        };
    }

    /// <summary>
    /// Resolves the attempt limit stored on a new job: the explicit value, else the job type's
    /// <see cref="RetryAttribute"/>, else <see cref="NexJobOptions.MaxAttempts"/>. Storing the resolved value
    /// keeps the record truthful for every storage path that compares attempts with the limit.
    /// </summary>
    /// <param name="jobType">The job type, or <see langword="null"/> when it cannot be loaded in this process.</param>
    /// <param name="explicitMaxAttempts">The value passed at the call site, if any.</param>
    /// <param name="options">The NexJob options.</param>
    /// <returns>The attempt limit to store, never below one.</returns>
    internal static int ResolveMaxAttempts(Type? jobType, int? explicitMaxAttempts, NexJobOptions options)
    {
        var attributeAttempts = jobType?.GetCustomAttribute<RetryAttribute>(inherit: true)?.Attempts;

        // [Retry(0)] and [Retry(1)] both mean "run once"; the stored limit is never below one.
        return Math.Max(1, explicitMaxAttempts ?? attributeAttempts ?? options.MaxAttempts);
    }
}
