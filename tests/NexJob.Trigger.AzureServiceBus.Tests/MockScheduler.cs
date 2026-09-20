using NexJob.Storage;

namespace NexJob.Trigger.AzureServiceBus.Tests;

/// <summary>
/// Mock scheduler for testing. Tracks enqueue calls and optionally simulates failures.
/// </summary>
internal sealed class MockScheduler : IScheduler
{
    private readonly List<JobRecord> _enqueueCalls = [];
    private readonly object _lock = new();

    public bool ShouldFailEnqueue { get; set; }

    public Exception? CustomException { get; set; }

    public IReadOnlyList<JobRecord> EnqueueCalls
    {
        get
        {
            lock (_lock)
            {
                return _enqueueCalls.ToList();
            }
        }
    }

    public Task<JobId> EnqueueAsync(
        JobRecord job,
        DuplicatePolicy duplicatePolicy = DuplicatePolicy.AllowAfterFailed,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ShouldFailEnqueue)
        {
            throw CustomException ?? new InvalidOperationException("Simulated enqueue failure");
        }

        lock (_lock)
        {
            _enqueueCalls.Add(job);
        }

        return Task.FromResult(job.Id);
    }

    public Task<JobId> EnqueueAsync<TJob>(
        string? queue = null,
        JobPriority priority = JobPriority.Normal,
        string? idempotencyKey = null,
        DuplicatePolicy duplicatePolicy = DuplicatePolicy.AllowAfterFailed,
        IReadOnlyList<string>? tags = null,
        TimeSpan? deadlineAfter = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob =>
        throw new NotSupportedException();

    public Task<JobId> EnqueueAsync<TJob, TInput>(
        TInput input,
        string? queue = null,
        JobPriority priority = JobPriority.Normal,
        string? idempotencyKey = null,
        DuplicatePolicy duplicatePolicy = DuplicatePolicy.AllowAfterFailed,
        IReadOnlyList<string>? tags = null,
        TimeSpan? deadlineAfter = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput> =>
        throw new NotSupportedException();

    public Task<JobId> ScheduleAsync<TJob>(
        TimeSpan delay,
        string? queue = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob =>
        throw new NotSupportedException();

    public Task<JobId> ScheduleAsync<TJob, TInput>(
        TInput input,
        TimeSpan delay,
        string? queue = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput> =>
        throw new NotSupportedException();

    public Task<JobId> ScheduleAtAsync<TJob>(
        DateTimeOffset runAt,
        string? queue = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob =>
        throw new NotSupportedException();

    public Task<JobId> ScheduleAtAsync<TJob, TInput>(
        TInput input,
        DateTimeOffset runAt,
        string? queue = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput> =>
        throw new NotSupportedException();

    public Task RecurringAsync<TJob, TInput>(
        string recurringJobId,
        TInput input,
        string cron,
        TimeZoneInfo? timeZone = null,
        string? queue = null,
        RecurringConcurrencyPolicy concurrencyPolicy = RecurringConcurrencyPolicy.SkipIfRunning,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput> =>
        throw new NotSupportedException();

    public Task RecurringAsync<TJob>(
        string recurringJobId,
        string cron,
        TimeZoneInfo? timeZone = null,
        string? queue = null,
        RecurringConcurrencyPolicy concurrencyPolicy = RecurringConcurrencyPolicy.SkipIfRunning,
        CancellationToken cancellationToken = default)
        where TJob : IJob =>
        throw new NotSupportedException();

    public Task<JobId> ContinueWithAsync<TJob, TInput>(
        JobId parentJobId,
        TInput input,
        string? queue = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput> =>
        throw new NotSupportedException();

    public Task<JobId> ContinueWithAsync<TJob>(
        JobId parentJobId,
        string? queue = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob =>
        throw new NotSupportedException();

    public Task RemoveRecurringAsync(string recurringJobId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<JobRecord>> GetJobsByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
