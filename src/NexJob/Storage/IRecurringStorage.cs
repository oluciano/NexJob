namespace NexJob.Storage;

/// <summary>
/// Storage contract for recurring job definitions and scheduling coordination.
/// Used by RecurringJobSchedulerService and RecurringJobRegistrar.
/// </summary>
public interface IRecurringStorage
{
    /// <summary>Creates or updates a recurring job definition.</summary>
    /// <param name="recurringJob">The recurring job record to persist.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task UpsertRecurringJobAsync(
        RecurringJobRecord recurringJob,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves all recurring jobs that are due for execution based on their cron schedule.</summary>
    /// <param name="utcNow">The current UTC time to compare against schedules.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A collection of due recurring jobs.</returns>
    Task<IReadOnlyList<RecurringJobRecord>> GetDueRecurringJobsAsync(
        DateTimeOffset utcNow,
        CancellationToken cancellationToken = default);

    /// <summary>Updates the next scheduled execution time for a recurring job.</summary>
    /// <param name="recurringJobId">The unique identifier of the recurring job.</param>
    /// <param name="nextExecution">The next UTC timestamp when the job should run.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SetRecurringJobNextExecutionAsync(
        string recurringJobId,
        DateTimeOffset nextExecution,
        CancellationToken cancellationToken = default);

    /// <summary>Updates the result of the last execution for a recurring job.</summary>
    /// <param name="recurringJobId">The unique identifier of the recurring job.</param>
    /// <param name="status">The terminal status of the last execution.</param>
    /// <param name="errorMessage">Optional error message if the execution failed.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task SetRecurringJobLastExecutionResultAsync(
        string recurringJobId,
        JobStatus status,
        string? errorMessage,
        CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a recurring job by marking it as deleted by the user.</summary>
    /// <param name="recurringJobId">The unique identifier of the recurring job.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task DeleteRecurringJobAsync(
        string recurringJobId,
        CancellationToken cancellationToken = default);

    /// <summary>Updates the runtime configuration for a recurring job.</summary>
    /// <param name="recurringJobId">The unique identifier of the recurring job.</param>
    /// <param name="cronOverride">Optional cron override to bypass the default schedule.</param>
    /// <param name="enabled">Whether the job is currently enabled for scheduling.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task UpdateRecurringJobConfigAsync(
        string recurringJobId,
        string? cronOverride,
        bool enabled,
        CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes a recurring job definition and all related records.</summary>
    /// <param name="recurringJobId">The unique identifier of the recurring job.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task ForceDeleteRecurringJobAsync(
        string recurringJobId,
        CancellationToken cancellationToken = default);

    /// <summary>Restores a previously soft-deleted recurring job.</summary>
    /// <param name="recurringJobId">The unique identifier of the recurring job.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    Task RestoreRecurringJobAsync(
        string recurringJobId,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves all registered recurring jobs.</summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A collection of all recurring job records.</returns>
    Task<IReadOnlyList<RecurringJobRecord>> GetRecurringJobsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves a specific recurring job by its ID, including deleted ones.</summary>
    /// <param name="recurringJobId">The unique identifier of the recurring job.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The recurring job record if found; otherwise null.</returns>
    Task<RecurringJobRecord?> GetRecurringJobByIdAsync(
        string recurringJobId,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves a specific recurring job by its ID, filtering out deleted ones by default depending on implementation.</summary>
    /// <param name="recurringJobId">The unique identifier of the recurring job.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The recurring job record if found; otherwise null.</returns>
    Task<RecurringJobRecord?> GetRecurringJobAsync(
        string recurringJobId,
        CancellationToken cancellationToken = default);

    /// <summary>Attempts to acquire a distributed lock for a recurring job execution.</summary>
    /// <param name="recurringJobId">The unique identifier of the recurring job.</param>
    /// <param name="ttl">The time-to-live for the lock.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>True if the lock was acquired; otherwise false.</returns>
    Task<bool> TryAcquireRecurringJobLockAsync(
        string recurringJobId,
        TimeSpan ttl,
        CancellationToken ct = default);

    /// <summary>Releases a previously acquired distributed lock for a recurring job.</summary>
    /// <param name="recurringJobId">The unique identifier of the recurring job.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task ReleaseRecurringJobLockAsync(
        string recurringJobId,
        CancellationToken ct = default);
}
