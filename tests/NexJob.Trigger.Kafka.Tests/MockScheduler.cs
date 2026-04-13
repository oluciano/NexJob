using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NexJob.Trigger.Kafka.Tests;

/// <summary>
/// Mock scheduler for testing. Tracks enqueue calls and optionally simulates failures.
/// </summary>
internal sealed class MockScheduler : IScheduler
{
    private readonly List<JobRecord> _enqueueCalls = [];
    private readonly TaskCompletionSource<bool> _enqueueTcs = new();
    private readonly TaskCompletionSource<bool> _enqueueAttemptTcs = new();
    private readonly object _lock = new();

    /// <summary>
    /// Gets or sets a value indicating whether enqueue should fail.
    /// </summary>
    public bool ShouldFailEnqueue { get; set; }

    /// <summary>
    /// Gets or sets the delay for enqueue operations.
    /// </summary>
    public TimeSpan EnqueueDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Gets the list of jobs that were enqueued.
    /// </summary>
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

    /// <inheritdoc/>
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

        // Note: job is added to _enqueueCalls before checking ShouldFailEnqueue.
        // On simulated failure, EnqueueCalls will contain the job even though enqueue
        // did not succeed. Tests checking failure scenarios should assert on the
        // broker ack/nack behaviour, not on EnqueueCalls count.
        if (ShouldFailEnqueue)
        {
            throw new InvalidOperationException("Simulated enqueue failure");
        }

        _enqueueTcs.TrySetResult(true);
        return job.Id;
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public Task<JobId> ScheduleAsync<TJob>(
        TimeSpan delay,
        string? queue = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<JobId> ScheduleAsync<TJob, TInput>(
        TInput input,
        TimeSpan delay,
        string? queue = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput> =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<JobId> ScheduleAtAsync<TJob>(
        DateTimeOffset runAt,
        string? queue = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<JobId> ScheduleAtAsync<TJob, TInput>(
        TInput input,
        DateTimeOffset runAt,
        string? queue = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput> =>
        throw new NotSupportedException();

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public Task RecurringAsync<TJob>(
        string recurringJobId,
        string cron,
        TimeZoneInfo? timeZone = null,
        string? queue = null,
        RecurringConcurrencyPolicy concurrencyPolicy = RecurringConcurrencyPolicy.SkipIfRunning,
        CancellationToken cancellationToken = default)
        where TJob : IJob =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<JobId> ContinueWithAsync<TJob, TInput>(
        JobId parentJobId,
        TInput input,
        string? queue = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob<TInput> =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<JobId> ContinueWithAsync<TJob>(
        JobId parentJobId,
        string? queue = null,
        CancellationToken cancellationToken = default)
        where TJob : IJob =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task RemoveRecurringAsync(string recurringJobId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <inheritdoc/>
    public Task<IReadOnlyList<JobRecord>> GetJobsByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>
    /// Waits for a job to be enqueued.
    /// </summary>
    public Task WaitForEnqueueAsync(CancellationToken cancellationToken) => _enqueueTcs.Task.WaitAsync(cancellationToken);

    /// <summary>
    /// Waits for an enqueue attempt.
    /// </summary>
    public Task WaitForEnqueueAttemptAsync(CancellationToken cancellationToken) => _enqueueAttemptTcs.Task.WaitAsync(cancellationToken);
}
