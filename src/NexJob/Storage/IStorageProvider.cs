namespace NexJob.Storage;

/// <summary>
/// Abstraction over the persistence layer.
/// Implement this to create a custom NexJob storage adapter.
/// </summary>
public interface IStorageProvider
{
    Task<JobId> EnqueueAsync(JobRecord job, CancellationToken cancellationToken = default);
    Task<JobRecord?> FetchNextAsync(IReadOnlyList<string> queues, CancellationToken cancellationToken = default);
    Task AcknowledgeAsync(JobId jobId, CancellationToken cancellationToken = default);
    Task SetFailedAsync(JobId jobId, Exception exception, DateTimeOffset? retryAt, CancellationToken cancellationToken = default);
    Task UpsertRecurringJobAsync(RecurringJobRecord recurringJob, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RecurringJobRecord>> GetDueRecurringJobsAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    Task SetRecurringJobNextExecutionAsync(string recurringJobId, DateTimeOffset nextExecution, CancellationToken cancellationToken = default);
    Task DeleteRecurringJobAsync(string recurringJobId, CancellationToken cancellationToken = default);
    Task RequeueOrphanedJobsAsync(TimeSpan heartbeatTimeout, CancellationToken cancellationToken = default);
    Task EnqueueContinuationsAsync(JobId parentJobId, CancellationToken cancellationToken = default);
}
