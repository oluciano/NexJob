using System.Collections.Concurrent;
using System.Threading.Channels;
using NexJob.Storage;

namespace NexJob.Internal;

/// <summary>
/// Thread-safe, in-process storage provider backed by <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// and <see cref="Channel{T}"/> priority queues. Intended for development and unit testing only —
/// all state is lost when the process restarts.
/// </summary>
internal sealed class InMemoryStorageProvider : IStorageProvider
{
    // ─── state ───────────────────────────────────────────────────────────────

    private readonly ConcurrentDictionary<Guid, JobRecord> _jobs = new();
    private readonly ConcurrentDictionary<string, Guid> _idempotencyIndex = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RecurringJobRecord> _recurringJobs = new(StringComparer.Ordinal);

    // Indexed as: _queues[queueName][priorityIndex]
    // Priority indices: 0=Critical, 1=High, 2=Normal, 3=Low
    private readonly ConcurrentDictionary<string, Channel<Guid>[]> _queues =
        new(StringComparer.Ordinal);

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static int PriorityIndex(JobPriority priority) => priority switch
    {
        JobPriority.Critical => 0,
        JobPriority.High     => 1,
        JobPriority.Normal   => 2,
        JobPriority.Low      => 3,
        _                    => 2,
    };

    private Channel<Guid>[] GetOrCreateQueueChannels(string queue) =>
        _queues.GetOrAdd(queue, static _ =>
        [
            Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false }),
            Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false }),
            Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false }),
            Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false }),
        ]);

    private void WriteToChannel(JobRecord job)
    {
        var channels = GetOrCreateQueueChannels(job.Queue);
        channels[PriorityIndex(job.Priority)].Writer.TryWrite(job.Id.Value);
    }

    // ─── IStorageProvider ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<JobId> EnqueueAsync(JobRecord job, CancellationToken cancellationToken = default)
    {
        // Idempotency check
        if (job.IdempotencyKey is not null)
        {
            if (_idempotencyIndex.TryGetValue(job.IdempotencyKey, out var existingId) &&
                _jobs.TryGetValue(existingId, out var existingJob) &&
                existingJob.Status is JobStatus.Enqueued or JobStatus.Scheduled or JobStatus.Processing)
            {
                return Task.FromResult(existingJob.Id);
            }

            // Expired or missing — clean up stale entry
            _idempotencyIndex.TryRemove(job.IdempotencyKey, out _);
        }

        _jobs[job.Id.Value] = job;

        if (job.IdempotencyKey is not null)
            _idempotencyIndex[job.IdempotencyKey] = job.Id.Value;

        // Only push to channel when immediately runnable (not scheduled for future)
        if (job.Status == JobStatus.Enqueued && job.ScheduledAt is null)
            WriteToChannel(job);

        return Task.FromResult(job.Id);
    }

    /// <inheritdoc/>
    public async Task<JobRecord?> FetchNextAsync(IReadOnlyList<string> queues, CancellationToken cancellationToken = default)
    {
        // Promote any due scheduled/retry jobs before attempting to dequeue
        PromoteDueScheduledJobs();

        foreach (var queue in queues)
        {
            var channels = GetOrCreateQueueChannels(queue);

            for (var i = 0; i < channels.Length; i++)
            {
                while (channels[i].Reader.TryRead(out var jobId))
                {
                    if (!_jobs.TryGetValue(jobId, out var job))
                        continue;

                    // Atomic claim: lock on the job instance to prevent double-processing
                    lock (job)
                    {
                        if (job.Status != JobStatus.Enqueued)
                            continue;

                        job.Status = JobStatus.Processing;
                        job.ProcessingStartedAt = DateTimeOffset.UtcNow;
                        job.HeartbeatAt = DateTimeOffset.UtcNow;
                        job.Attempts++;
                        return Task.FromResult<JobRecord?>(job).Result;
                    }
                }
            }
        }

        // No job available — yield briefly so callers can back off
        await Task.Delay(100, cancellationToken);
        return null;
    }

    /// <inheritdoc/>
    public Task AcknowledgeAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId.Value, out var job))
        {
            lock (job)
            {
                job.Status = JobStatus.Succeeded;
                job.CompletedAt = DateTimeOffset.UtcNow;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetFailedAsync(JobId jobId, Exception exception, DateTimeOffset? retryAt, CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(jobId.Value, out var job))
            return Task.CompletedTask;

        lock (job)
        {
            job.LastErrorMessage = exception.Message;
            job.LastErrorStackTrace = exception.StackTrace;

            if (retryAt.HasValue)
            {
                // Will be promoted to Enqueued when ScheduledAt becomes due
                job.Status = JobStatus.Scheduled;
                job.RetryAt = retryAt.Value;
            }
            else
            {
                job.Status = JobStatus.Failed;
                job.CompletedAt = DateTimeOffset.UtcNow;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpdateHeartbeatAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId.Value, out var job))
            job.HeartbeatAt = DateTimeOffset.UtcNow;

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpsertRecurringJobAsync(RecurringJobRecord recurringJob, CancellationToken cancellationToken = default)
    {
        _recurringJobs[recurringJob.RecurringJobId] = recurringJob;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<RecurringJobRecord>> GetDueRecurringJobsAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RecurringJobRecord> due = _recurringJobs.Values
            .Where(r => r.NextExecution.HasValue && r.NextExecution.Value <= utcNow)
            .ToList();

        return Task.FromResult(due);
    }

    /// <inheritdoc/>
    public Task SetRecurringJobNextExecutionAsync(string recurringJobId, DateTimeOffset nextExecution, CancellationToken cancellationToken = default)
    {
        if (_recurringJobs.TryGetValue(recurringJobId, out var job))
        {
            job.LastExecutedAt = DateTimeOffset.UtcNow;
            job.NextExecution = nextExecution;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteRecurringJobAsync(string recurringJobId, CancellationToken cancellationToken = default)
    {
        _recurringJobs.TryRemove(recurringJobId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RequeueOrphanedJobsAsync(TimeSpan heartbeatTimeout, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - heartbeatTimeout;

        foreach (var job in _jobs.Values)
        {
            if (job.Status != JobStatus.Processing)
                continue;

            if (job.HeartbeatAt.HasValue && job.HeartbeatAt.Value >= cutoff)
                continue;

            lock (job)
            {
                if (job.Status != JobStatus.Processing)
                    continue;

                if (job.HeartbeatAt.HasValue && job.HeartbeatAt.Value >= cutoff)
                    continue;

                job.Status = JobStatus.Enqueued;
                job.ProcessingStartedAt = null;
                job.HeartbeatAt = null;
                WriteToChannel(job);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task EnqueueContinuationsAsync(JobId parentJobId, CancellationToken cancellationToken = default)
    {
        foreach (var job in _jobs.Values)
        {
            if (job.Status != JobStatus.AwaitingContinuation || job.ParentJobId != parentJobId)
                continue;

            lock (job)
            {
                if (job.Status != JobStatus.AwaitingContinuation)
                    continue;

                job.Status = JobStatus.Enqueued;
                WriteToChannel(job);
            }
        }

        return Task.CompletedTask;
    }

    // ─── private helpers ─────────────────────────────────────────────────────

    private void PromoteDueScheduledJobs()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var job in _jobs.Values)
        {
            if (job.Status != JobStatus.Scheduled)
                continue;

            var dueAt = job.RetryAt ?? job.ScheduledAt;
            if (!dueAt.HasValue || dueAt.Value > now)
                continue;

            lock (job)
            {
                if (job.Status != JobStatus.Scheduled)
                    continue;

                dueAt = job.RetryAt ?? job.ScheduledAt;
                if (!dueAt.HasValue || dueAt.Value > now)
                    continue;

                job.Status = JobStatus.Enqueued;
                WriteToChannel(job);
            }
        }
    }
}
