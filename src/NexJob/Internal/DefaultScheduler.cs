using System.Diagnostics;
using System.Text.Json;
using Cronos;
using NexJob.Storage;
using NexJob.Telemetry;

namespace NexJob.Internal;

/// <summary>
/// Default implementation of <see cref="IScheduler"/>. Serializes job inputs with
/// <see cref="JsonSerializer"/> and delegates persistence to <see cref="IStorageProvider"/>.
/// </summary>
internal sealed class DefaultScheduler : IScheduler
{
    private readonly IStorageProvider _storage;
    private readonly NexJobOptions _options;

    /// <summary>
    /// Initializes a new <see cref="DefaultScheduler"/>.
    /// </summary>
    /// <param name="storage">The storage provider used to persist job records.</param>
    /// <param name="options">Global NexJob configuration options.</param>
    public DefaultScheduler(IStorageProvider storage, NexJobOptions options)
    {
        _storage = storage;
        _options = options;
    }

    /// <inheritdoc/>
    public async Task<JobId> EnqueueAsync<TJob, TInput>(
        TInput input,
        string? queue = null,
        JobPriority priority = JobPriority.Normal,
        string? idempotencyKey = null,
        IReadOnlyList<string>? tags = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput>
    {
        var job = BuildJobRecord<TJob, TInput>(input, queue, priority, idempotencyKey,
            status: JobStatus.Enqueued, scheduledAt: null, tags: tags);

        using var activity = NexJobActivitySource.StartEnqueue(typeof(TJob).FullName ?? typeof(TJob).Name, job.Queue);

        var jobId = await _storage.EnqueueAsync(job, cancellationToken);

        activity?.SetTag("nexjob.job_id", jobId.Value.ToString());
        NexJobMetrics.JobsEnqueued.Add(1, new TagList { { "nexjob.job_type", typeof(TJob).Name }, { "nexjob.queue", job.Queue } });

        return jobId;
    }

    /// <inheritdoc/>
    public Task<JobId> ScheduleAsync<TJob, TInput>(
        TInput input,
        TimeSpan delay,
        string? queue = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput>
    {
        var scheduledAt = DateTimeOffset.UtcNow + delay;
        var job = BuildJobRecord<TJob, TInput>(input, queue, JobPriority.Normal, idempotencyKey,
            status: JobStatus.Scheduled, scheduledAt: scheduledAt);

        return _storage.EnqueueAsync(job, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<JobId> ScheduleAtAsync<TJob, TInput>(
        TInput input,
        DateTimeOffset runAt,
        string? queue = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput>
    {
        var job = BuildJobRecord<TJob, TInput>(input, queue, JobPriority.Normal, idempotencyKey,
            status: JobStatus.Scheduled, scheduledAt: runAt);

        return _storage.EnqueueAsync(job, cancellationToken);
    }

    /// <inheritdoc/>
    public Task RecurringAsync<TJob, TInput>(
        string recurringJobId,
        TInput input,
        string cron,
        TimeZoneInfo? timeZone = null,
        string? queue = null,
        RecurringConcurrencyPolicy concurrencyPolicy = RecurringConcurrencyPolicy.SkipIfRunning,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput>
    {
        var tz = timeZone ?? TimeZoneInfo.Utc;
        var cronExpression = ParseCron(cron);
        var nextExecution = cronExpression.GetNextOccurrence(DateTimeOffset.UtcNow, tz);

        var record = new RecurringJobRecord
        {
            RecurringJobId = recurringJobId,
            JobType = typeof(TJob).AssemblyQualifiedName!,
            InputType = typeof(TInput).AssemblyQualifiedName!,
            InputJson = JsonSerializer.Serialize(input),
            Cron = cron,
            TimeZoneId = timeZone?.Id,
            Queue = queue ?? "default",
            NextExecution = nextExecution,
            CreatedAt = DateTimeOffset.UtcNow,
            ConcurrencyPolicy = concurrencyPolicy,
        };

        return _storage.UpsertRecurringJobAsync(record, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<JobId> ContinueWithAsync<TJob, TInput>(
        JobId parentJobId,
        TInput input,
        string? queue = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput>
    {
        var traceParent = Activity.Current?.Id;

        var job = new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(TJob).AssemblyQualifiedName!,
            InputType = typeof(TInput).AssemblyQualifiedName!,
            InputJson = JsonSerializer.Serialize(input),
            Queue = queue ?? "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.AwaitingContinuation,
            ParentJobId = parentJobId,
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = _options.MaxAttempts,
            TraceParent = traceParent,
        };

        return await _storage.EnqueueAsync(job, cancellationToken);
    }

    /// <inheritdoc/>
    public Task RemoveRecurringAsync(string recurringJobId, CancellationToken cancellationToken = default) =>
        _storage.DeleteRecurringJobAsync(recurringJobId, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<JobRecord>> GetJobsByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        _storage.GetJobsByTagAsync(tag, cancellationToken);

    // ─── helpers ─────────────────────────────────────────────────────────────

    internal static CronExpression ParseCron(string cron)
    {
        // Try 6-field (with seconds) first; fall back to standard 5-field
        try
        {
            return CronExpression.Parse(cron, CronFormat.IncludeSeconds);
        }
        catch (CronFormatException)
        {
            return CronExpression.Parse(cron, CronFormat.Standard);
        }
    }

    private JobRecord BuildJobRecord<TJob, TInput>(
        TInput input,
        string? queue,
        JobPriority priority,
        string? idempotencyKey,
        JobStatus status,
        DateTimeOffset? scheduledAt,
        IReadOnlyList<string>? tags = null)
    {
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
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = _options.MaxAttempts,
            Tags = tags ?? [],
        };
    }
}
