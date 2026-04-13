using System.Diagnostics;
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
        JobId? parentJobId = null)
        where TJob : IJob<TInput>
    {
        var capturedTraceParent = traceParent ?? Activity.Current?.Id;

        return new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(TJob).AssemblyQualifiedName!,
            InputType = typeof(TInput).AssemblyQualifiedName!,
            InputJson = JsonSerializer.Serialize(input),
            Queue = queue ?? "default",
            Priority = priority,
            Status = status,
            IdempotencyKey = idempotencyKey,
            ScheduledAt = scheduledAt,
            ExpiresAt = expiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = options.MaxAttempts,
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
        JobId? parentJobId = null)
        where TJob : IJob
    {
        var capturedTraceParent = traceParent ?? Activity.Current?.Id;

        return new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(TJob).AssemblyQualifiedName!,
            InputType = typeof(NoInput).AssemblyQualifiedName!,
            InputJson = JsonSerializer.Serialize(NoInput.Instance),
            Queue = queue ?? "default",
            Priority = priority,
            Status = status,
            IdempotencyKey = idempotencyKey,
            ScheduledAt = scheduledAt,
            ExpiresAt = expiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = options.MaxAttempts,
            TraceParent = capturedTraceParent,
            Tags = tags ?? [],
            ParentJobId = parentJobId,
        };
    }
}
