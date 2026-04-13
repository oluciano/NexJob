using NexJob.Storage;

namespace NexJob.Trigger.AwsSqs.Tests;

/// <summary>
/// Mock storage provider for testing. Tracks enqueue calls and optionally simulates failures.
/// </summary>
internal sealed class MockStorageProvider : IStorageProvider
{
    private readonly List<JobRecord> _enqueueCalls = new();
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

    public async Task<EnqueueResult> EnqueueAsync(
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
        return new EnqueueResult(job.Id, false);
    }

    public Task WaitForEnqueueAsync(CancellationToken cancellationToken) => _enqueueTcs.Task.WaitAsync(cancellationToken);
    public Task WaitForEnqueueAttemptAsync(CancellationToken cancellationToken) => _enqueueAttemptTcs.Task.WaitAsync(cancellationToken);

    // ─── IStorageProvider members not used in tests ──────────────────────────

    public Task<JobRecord?> FetchNextAsync(IReadOnlyList<string> queues, CancellationToken cancellationToken = default) =>
        Task.FromResult<JobRecord?>(null);

    public Task AcknowledgeAsync(JobId jobId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetFailedAsync(JobId jobId, Exception exception, DateTimeOffset? retryAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetExpiredAsync(JobId jobId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UpdateHeartbeatAsync(JobId jobId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UpsertRecurringJobAsync(RecurringJobRecord recurringJob, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyList<RecurringJobRecord>> GetDueRecurringJobsAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RecurringJobRecord>>(Array.Empty<RecurringJobRecord>());
    public Task SetRecurringJobNextExecutionAsync(string recurringJobId, DateTimeOffset nextExecution, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetRecurringJobLastExecutionResultAsync(string recurringJobId, JobStatus status, string? errorMessage, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteRecurringJobAsync(string recurringJobId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UpdateRecurringJobConfigAsync(string recurringJobId, string? cronOverride, bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ForceDeleteRecurringJobAsync(string recurringJobId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RestoreRecurringJobAsync(string recurringJobId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyList<RecurringJobRecord>> GetRecurringJobsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RecurringJobRecord>>(Array.Empty<RecurringJobRecord>());
    public Task<RecurringJobRecord?> GetRecurringJobByIdAsync(string recurringJobId, CancellationToken cancellationToken = default) =>
        Task.FromResult<RecurringJobRecord?>(null);
    public Task<RecurringJobRecord?> GetRecurringJobAsync(string recurringJobId, CancellationToken cancellationToken = default) =>
        Task.FromResult<RecurringJobRecord?>(null);
    public Task RequeueOrphanedJobsAsync(TimeSpan heartbeatTimeout, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task EnqueueContinuationsAsync(JobId parentJobId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RegisterServerAsync(ServerRecord server, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task HeartbeatServerAsync(string serverId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeregisterServerAsync(string serverId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyList<ServerRecord>> GetActiveServersAsync(TimeSpan activeTimeout, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ServerRecord>>(Array.Empty<ServerRecord>());
    public Task<JobMetrics> GetMetricsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new JobMetrics());
    public Task<PagedResult<JobRecord>> GetJobsAsync(JobFilter filter, int page, int pageSize, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PagedResult<JobRecord> { Items = Array.Empty<JobRecord>(), TotalCount = 0, Page = page, PageSize = pageSize });
    public Task<JobRecord?> GetJobByIdAsync(JobId id, CancellationToken cancellationToken = default) =>
        Task.FromResult<JobRecord?>(null);
    public Task DeleteJobAsync(JobId id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RequeueJobAsync(JobId id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SaveExecutionLogsAsync(JobId jobId, IReadOnlyList<JobExecutionLog> logs, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CommitJobResultAsync(JobId jobId, JobExecutionResult result, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyList<QueueMetrics>> GetQueueMetricsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<QueueMetrics>>(Array.Empty<QueueMetrics>());
    public Task<bool> TryAcquireRecurringJobLockAsync(string recurringJobId, TimeSpan ttl, CancellationToken ct = default) =>
        Task.FromResult(false);
    public Task ReleaseRecurringJobLockAsync(string recurringJobId, CancellationToken ct = default) => Task.CompletedTask;
    public Task ReportProgressAsync(JobId jobId, int percent, string? message, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<JobRecord>> GetJobsByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<JobRecord>>(Array.Empty<JobRecord>());
    public Task<int> PurgeJobsAsync(RetentionPolicy policy, CancellationToken cancellationToken = default) => Task.FromResult(0);
}
