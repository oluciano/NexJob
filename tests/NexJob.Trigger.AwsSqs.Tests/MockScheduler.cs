namespace NexJob.Trigger.AwsSqs.Tests;

/// <summary>
/// Mock scheduler for testing. Tracks enqueue calls and optionally simulates failures.
/// </summary>
internal sealed class MockScheduler : IScheduler
{
    private readonly List<JobRecord> _enqueueCalls = [];
    private readonly TaskCompletionSource<bool> _enqueueTcs = new();
    private readonly TaskCompletionSource<bool> _enqueueAttemptTcs = new();
    private readonly object _lock = new();

    public bool ShouldFailEnqueue { get; set; }
    public TimeSpan EnqueueDelay { get; set; } = TimeSpan.Zero;

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

    public async Task<JobId> EnqueueAsync(
        JobRecord job,
        DuplicatePolicy duplicatePolicy = DuplicatePolicy.AllowAfterFailed,
        CancellationToken cancellationToken = default)
    {
        _enqueueAttemptTcs.TrySetResult(true);

        if (EnqueueDelay > TimeSpan.Zero)
        {
            await Task.Delay(EnqueueDelay, cancellationToken).ConfigureAwait(false);
        }

        lock (_lock)
        {
            _enqueueCalls.Add(job);
        }

        if (ShouldFailEnqueue)
        {
            throw new InvalidOperationException("Simulated enqueue failure");
        }

        _enqueueTcs.TrySetResult(true);
        return job.Id;
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
        throw new NotSupportedException("This mock only supports the non-generic EnqueueAsync overload.");

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
        throw new NotSupportedException("This mock only supports the non-generic EnqueueAsync overload.");

    public Task<JobId> ScheduleAsync<TJob>(
        TimeSpan delay,
        string? queue = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob =>
        throw new NotSupportedException("This mock only supports the non-generic EnqueueAsync overload.");

    public Task<JobId> ScheduleAsync<TJob, TInput>(
        TInput input,
        TimeSpan delay,
        string? queue = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput> =>
        throw new NotSupportedException("This mock only supports the non-generic EnqueueAsync overload.");

    public Task<JobId> ScheduleAtAsync<TJob>(
        DateTimeOffset runAt,
        string? queue = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob =>
        throw new NotSupportedException("This mock only supports the non-generic EnqueueAsync overload.");

    public Task<JobId> ScheduleAtAsync<TJob, TInput>(
        TInput input,
        DateTimeOffset runAt,
        string? queue = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput> =>
        throw new NotSupportedException("This mock only supports the non-generic EnqueueAsync overload.");

    public Task RecurringAsync<TJob, TInput>(
        string recurringJobId,
        TInput input,
        string cron,
        TimeZoneInfo? timeZone = null,
        string? queue = null,
        RecurringConcurrencyPolicy concurrencyPolicy = RecurringConcurrencyPolicy.SkipIfRunning,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput> =>
        throw new NotSupportedException("This mock only supports the non-generic EnqueueAsync overload.");

    public Task RecurringAsync<TJob>(
        string recurringJobId,
        string cron,
        TimeZoneInfo? timeZone = null,
        string? queue = null,
        RecurringConcurrencyPolicy concurrencyPolicy = RecurringConcurrencyPolicy.SkipIfRunning,
        CancellationToken cancellationToken = default)
        where TJob : IJob =>
        throw new NotSupportedException("This mock only supports the non-generic EnqueueAsync overload.");

    public Task<JobId> ContinueWithAsync<TJob, TInput>(
        JobId parentJobId,
        TInput input,
        string? queue = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput> =>
        throw new NotSupportedException("This mock only supports the non-generic EnqueueAsync overload.");

    public Task<JobId> ContinueWithAsync<TJob>(
        JobId parentJobId,
        string? queue = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob =>
        throw new NotSupportedException("This mock only supports the non-generic EnqueueAsync overload.");

    public Task RemoveRecurringAsync(string recurringJobId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This mock only supports the non-generic EnqueueAsync overload.");

    public Task<IReadOnlyList<JobRecord>> GetJobsByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This mock only supports the non-generic EnqueueAsync overload.");

    public Task WaitForEnqueueAsync(CancellationToken cancellationToken) => _enqueueTcs.Task.WaitAsync(cancellationToken);

    public Task WaitForEnqueueAttemptAsync(CancellationToken cancellationToken) => _enqueueAttemptTcs.Task.WaitAsync(cancellationToken);
}
