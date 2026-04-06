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
    private readonly ConcurrentDictionary<string, DateTimeOffset> _recurringLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ServerRecord> _servers = new(StringComparer.Ordinal);

    // Indexed as: _queues[queueName][priorityIndex]
    // Priority indices: 0=Critical, 1=High, 2=Normal, 3=Low
    private readonly ConcurrentDictionary<string, Channel<Guid>[]> _queues =
        new(StringComparer.Ordinal);

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
        {
            _idempotencyIndex[job.IdempotencyKey] = job.Id.Value;
        }

        // Only push to channel when immediately runnable (not scheduled for future)
        if (job.Status == JobStatus.Enqueued && job.ScheduledAt is null)
        {
            WriteToChannel(job);
        }

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
                    {
                        continue;
                    }

                    // Atomic claim: lock on the job instance to prevent double-processing
                    lock (job)
                    {
                        if (job.Status != JobStatus.Enqueued)
                        {
                            continue;
                        }

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
        await Task.Delay(100, cancellationToken).ConfigureAwait(false);
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
        {
            return Task.CompletedTask;
        }

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
    public Task SetExpiredAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId.Value, out var job))
        {
            lock (job)
            {
                job.Status = JobStatus.Expired;
                job.CompletedAt = DateTimeOffset.UtcNow;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpdateHeartbeatAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId.Value, out var job))
        {
            job.HeartbeatAt = DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpsertRecurringJobAsync(RecurringJobRecord recurringJob, CancellationToken cancellationToken = default)
    {
        _recurringJobs.AddOrUpdate(
            recurringJob.RecurringJobId,
            // Insert: use the record as-is (Enabled defaults to true, DeletedByUser defaults to false)
            _ => recurringJob,
            // Update: preserve user-set CronOverride, Enabled and DeletedByUser fields.
            // If the existing record was deleted by the user, keep it deleted — do not resurrect it.
            (_, existing) =>
            {
                recurringJob.CronOverride = existing.CronOverride;
                recurringJob.Enabled = existing.Enabled;
                recurringJob.DeletedByUser = existing.DeletedByUser;
                return recurringJob;
            });
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
    public Task SetRecurringJobLastExecutionResultAsync(string recurringJobId, JobStatus status, string? errorMessage, CancellationToken cancellationToken = default)
    {
        if (_recurringJobs.TryGetValue(recurringJobId, out var job))
        {
            job.LastExecutionStatus = status;
            job.LastExecutionError = errorMessage;
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
    public Task UpdateRecurringJobConfigAsync(
        string recurringJobId, string? cronOverride, bool enabled,
        CancellationToken cancellationToken = default)
    {
        if (_recurringJobs.TryGetValue(recurringJobId, out var job))
        {
            job.CronOverride = cronOverride;
            job.Enabled = enabled;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ForceDeleteRecurringJobAsync(string recurringJobId, CancellationToken cancellationToken = default)
    {
        if (_recurringJobs.TryGetValue(recurringJobId, out var job))
        {
            job.DeletedByUser = true;
            job.Enabled = false;
        }

        var toRemove = _jobs.Values
            .Where(j => string.Equals(j.RecurringJobId, recurringJobId, StringComparison.Ordinal))
            .Select(j => j.Id.Value)
            .ToList();

        foreach (var id in toRemove)
        {
            _jobs.TryRemove(id, out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RestoreRecurringJobAsync(string recurringJobId, CancellationToken cancellationToken = default)
    {
        if (_recurringJobs.TryGetValue(recurringJobId, out var job))
        {
            job.DeletedByUser = false;
            job.Enabled = true;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<RecurringJobRecord>> GetRecurringJobsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RecurringJobRecord> all = _recurringJobs.Values.ToList();
        return Task.FromResult(all);
    }

    /// <inheritdoc/>
    public Task<RecurringJobRecord?> GetRecurringJobByIdAsync(string recurringJobId, CancellationToken cancellationToken = default)
    {
        return GetRecurringJobAsync(recurringJobId, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<RecurringJobRecord?> GetRecurringJobAsync(string recurringJobId, CancellationToken cancellationToken = default)
    {
        _recurringJobs.TryGetValue(recurringJobId, out var record);
        return Task.FromResult(record);
    }

    /// <inheritdoc/>
    public Task RequeueOrphanedJobsAsync(TimeSpan heartbeatTimeout, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - heartbeatTimeout;

        foreach (var job in _jobs.Values)
        {
            if (job.Status != JobStatus.Processing)
            {
                continue;
            }

            if (job.HeartbeatAt.HasValue && job.HeartbeatAt.Value >= cutoff)
            {
                continue;
            }

            lock (job)
            {
                if (job.Status != JobStatus.Processing)
                {
                    continue;
                }

                if (job.HeartbeatAt.HasValue && job.HeartbeatAt.Value >= cutoff)
                {
                    continue;
                }

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
            {
                continue;
            }

            lock (job)
            {
                if (job.Status != JobStatus.AwaitingContinuation)
                {
                    continue;
                }

                job.Status = JobStatus.Enqueued;
                WriteToChannel(job);
            }
        }

        return Task.CompletedTask;
    }

    // ─── Server / Worker node tracking ────────────────────────────────────────

    /// <inheritdoc/>
    public Task RegisterServerAsync(ServerRecord server, CancellationToken cancellationToken = default)
    {
        _servers[server.Id] = server;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task HeartbeatServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        if (_servers.TryGetValue(serverId, out var server))
        {
            server.HeartbeatAt = DateTimeOffset.UtcNow;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeregisterServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        _servers.TryRemove(serverId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ServerRecord>> GetActiveServersAsync(TimeSpan activeTimeout, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - activeTimeout;
        IReadOnlyList<ServerRecord> active = _servers.Values
            .Where(s => s.HeartbeatAt >= cutoff)
            .ToList();

        return Task.FromResult(active);
    }

    // ─── Dashboard support ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<JobMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff24 = now.AddHours(-24);
        var jobs = _jobs.Values.ToList();

        // Per-hour throughput for the last 24 hours
        var throughput = jobs
            .Where(j => j.CompletedAt.HasValue && j.CompletedAt.Value >= cutoff24)
            .GroupBy(j => new DateTimeOffset(
                j.CompletedAt!.Value.Year, j.CompletedAt.Value.Month, j.CompletedAt.Value.Day,
                j.CompletedAt.Value.Hour, 0, 0, TimeSpan.Zero))
            .Select(g => new HourlyThroughput { Hour = g.Key, Count = g.Count() })
            .OrderBy(h => h.Hour)
            .ToList();

        var recentFailures = jobs
            .Where(j => j.Status == JobStatus.Failed)
            .OrderByDescending(j => j.CompletedAt)
            .Take(10)
            .ToList();

        var metrics = new JobMetrics
        {
            Enqueued = jobs.Count(j => j.Status == JobStatus.Enqueued),
            Processing = jobs.Count(j => j.Status == JobStatus.Processing),
            Succeeded = jobs.Count(j => j.Status == JobStatus.Succeeded),
            Failed = jobs.Count(j => j.Status == JobStatus.Failed),
            Scheduled = jobs.Count(j => j.Status == JobStatus.Scheduled),
            Expired = jobs.Count(j => j.Status == JobStatus.Expired),
            Recurring = _recurringJobs.Count,
            HourlyThroughput = throughput,
            RecentFailures = recentFailures,
        };

        return Task.FromResult(metrics);
    }

    /// <inheritdoc/>
    public Task<PagedResult<JobRecord>> GetJobsAsync(
        JobFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _jobs.Values.AsEnumerable();

        if (filter.Status.HasValue)
        {
            query = query.Where(j => j.Status == filter.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Queue))
        {
            query = query.Where(j => j.Queue.Equals(filter.Queue, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = query.Where(j =>
                j.JobType.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                j.Id.Value.ToString().Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(filter.RecurringJobId))
        {
            query = query.Where(j => string.Equals(j.RecurringJobId, filter.RecurringJobId, StringComparison.Ordinal));
        }

        var ordered = query.OrderByDescending(j => j.CreatedAt).ToList();
        var total = ordered.Count;
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult(new PagedResult<JobRecord>
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        });
    }

    /// <inheritdoc/>
    public Task<JobRecord?> GetJobByIdAsync(JobId id, CancellationToken cancellationToken = default)
    {
        _jobs.TryGetValue(id.Value, out var job);
        return Task.FromResult(job);
    }

    /// <inheritdoc/>
    public Task DeleteJobAsync(JobId id, CancellationToken cancellationToken = default)
    {
        _jobs.TryRemove(id.Value, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RequeueJobAsync(JobId id, CancellationToken cancellationToken = default)
    {
        if (!_jobs.TryGetValue(id.Value, out var job))
        {
            return Task.CompletedTask;
        }

        lock (job)
        {
            job.Status = JobStatus.Enqueued;
            job.Attempts = 0;
            job.RetryAt = null;
            job.CompletedAt = null;
            job.LastErrorMessage = null;
            job.LastErrorStackTrace = null;
            WriteToChannel(job);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<QueueMetrics>> GetQueueMetricsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<QueueMetrics> result = _jobs.Values
            .GroupBy(j => j.Queue, StringComparer.Ordinal)
            .Select(g => new QueueMetrics
            {
                Queue = g.Key,
                Enqueued = g.Count(j => j.Status == JobStatus.Enqueued),
                Processing = g.Count(j => j.Status == JobStatus.Processing),
            })
            .OrderBy(q => q.Queue, StringComparer.Ordinal)
            .ToList();

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task SaveExecutionLogsAsync(
        JobId jobId, IReadOnlyList<JobExecutionLog> logs,
        CancellationToken cancellationToken = default)
    {
        if (_jobs.TryGetValue(jobId.Value, out var job))
        {
            lock (job)
            {
                job.ExecutionLogs = logs;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<bool> TryAcquireRecurringJobLockAsync(
        string recurringJobId, TimeSpan ttl, CancellationToken ct = default)
    {
        var expiry = DateTimeOffset.UtcNow.Add(ttl);
        _recurringLocks.TryGetValue(recurringJobId, out var existing);
        if (existing > DateTimeOffset.UtcNow)
        {
            return Task.FromResult(false);
        }

        _recurringLocks[recurringJobId] = expiry;
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task ReleaseRecurringJobLockAsync(string recurringJobId, CancellationToken ct = default)
    {
        _recurringLocks.TryRemove(recurringJobId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ReportProgressAsync(JobId jobId, int percent, string? message, CancellationToken ct = default)
    {
        if (_jobs.TryGetValue(jobId.Value, out var job))
        {
            lock (job)
            {
                job.ProgressPercent = percent;
                job.ProgressMessage = message;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<JobRecord>> GetJobsByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<JobRecord> result = _jobs.Values
            .Where(j => j.Tags.Contains(tag, StringComparer.Ordinal))
            .ToList();
        return Task.FromResult(result);
    }

    // ─── private helpers ─────────────────────────────────────────────────────

    private static int PriorityIndex(JobPriority priority) => priority switch
    {
        JobPriority.Critical => 0,
        JobPriority.High => 1,
        JobPriority.Normal => 2,
        JobPriority.Low => 3,
        _ => 2,
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

    private void PromoteDueScheduledJobs()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var job in _jobs.Values)
        {
            if (job.Status != JobStatus.Scheduled)
            {
                continue;
            }

            var dueAt = job.RetryAt ?? job.ScheduledAt;
            if (!dueAt.HasValue || dueAt.Value > now)
            {
                continue;
            }

            lock (job)
            {
                if (job.Status != JobStatus.Scheduled)
                {
                    continue;
                }

                dueAt = job.RetryAt ?? job.ScheduledAt;
                if (!dueAt.HasValue || dueAt.Value > now)
                {
                    continue;
                }

                job.Status = JobStatus.Enqueued;
                WriteToChannel(job);
            }
        }
    }
}
