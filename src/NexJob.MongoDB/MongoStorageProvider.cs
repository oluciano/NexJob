using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using NexJob.Storage;

namespace NexJob.MongoDB;

/// <summary>
/// MongoDB-backed implementation of <see cref="IStorageProvider"/>.
/// Uses <c>FindOneAndUpdate</c> for atomic job claiming, preventing double-processing
/// across multiple workers or server instances.
/// </summary>
public sealed class MongoStorageProvider : IStorageProvider
{
    private readonly IMongoCollection<JobDocument> _jobs;
    private readonly IMongoCollection<RecurringJobDocument> _recurringJobs;
    private readonly IMongoCollection<BsonDocument> _recurringLocks;
    private readonly IMongoCollection<ServerDocument> _servers;

    static MongoStorageProvider()
    {
        // Register enum-as-string convention globally for NexJob documents
        var pack = new ConventionPack { new EnumRepresentationConvention(BsonType.String) };
        ConventionRegistry.Register("NexJobEnumAsString", pack, t =>
            t == typeof(JobDocument) || t == typeof(RecurringJobDocument));

        // Store DateTimeOffset as a UTC DateTime tick pair to preserve offset
        // TryRegisterSerializer may fail if already registered; swallow the exception gracefully
        try
        {
            BsonSerializer.TryRegisterSerializer(new DateTimeOffsetSerializer(BsonType.String));
        }
        catch (BsonSerializationException)
        {
            // Already registered, likely by another provider or test setup
        }
    }

    /// <summary>
    /// Initialises the provider using an existing <see cref="IMongoDatabase"/>.
    /// Indexes are created on construction (idempotent — safe to call multiple times).
    /// </summary>
    public MongoStorageProvider(IMongoDatabase database)
    {
        _jobs = database.GetCollection<JobDocument>("nexjob_jobs");
        _recurringJobs = database.GetCollection<RecurringJobDocument>("nexjob_recurring_jobs");
        _recurringLocks = database.GetCollection<BsonDocument>("nexjob_recurring_locks");
        _servers = database.GetCollection<ServerDocument>("nexjob_servers");

        EnsureIndexes();
    }

    // ── EnqueueAsync ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<EnqueueResult> EnqueueAsync(JobRecord job, DuplicatePolicy duplicatePolicy = DuplicatePolicy.AllowAfterFailed, CancellationToken cancellationToken = default)
    {
        if (job.IdempotencyKey is not null)
        {
            // Check for existing job with same idempotency key
            var filter = Builders<JobDocument>.Filter.Eq(d => d.IdempotencyKey, job.IdempotencyKey);
            var existing = await _jobs.Find(filter)
                .Sort(Builders<JobDocument>.Sort.Descending(d => d.CreatedAt))
                .Limit(1)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (existing is not null)
            {
                var existingId = existing.Id;
                var existingStatus = existing.Status;

                if (IsActiveState(existingStatus))
                {
                    return new EnqueueResult(existingId, WasRejected: false);
                }

                var reject = existingStatus == JobStatus.Failed
                    ? duplicatePolicy is DuplicatePolicy.RejectIfFailed or DuplicatePolicy.RejectAlways
                    : duplicatePolicy == DuplicatePolicy.RejectAlways;

                if (reject)
                {
                    return new EnqueueResult(existingId, WasRejected: true);
                }
            }
        }

        // Insert new job. If a race condition occurs and another thread inserted a job with the same
        // idempotency key after our check above, the unique index will throw DuplicateKey.
        // Catch it and apply duplicate policy to the existing job that won the race.
        try
        {
            await _jobs.InsertOneAsync(JobDocument.FromRecord(job), cancellationToken: cancellationToken).ConfigureAwait(false);
            return new EnqueueResult(job.Id, WasRejected: false);
        }
        catch (MongoWriteException ex) when (
            job.IdempotencyKey is not null &&
            ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Race condition: another writer inserted a job with the same idempotency key after our check.
            // Fetch the winning job and apply duplicate policy to it.
            var filter = Builders<JobDocument>.Filter.Eq(d => d.IdempotencyKey, job.IdempotencyKey);
            var winner = await _jobs.Find(filter)
                .Sort(Builders<JobDocument>.Sort.Ascending(d => d.CreatedAt))
                .Limit(1)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (winner is null)
            {
                // This should not happen, but if the winner was deleted between the error and our query, rethrow.
                throw;
            }

            var winnerId = winner.Id;
            var winnerStatus = winner.Status;

            if (IsActiveState(winnerStatus))
            {
                return new EnqueueResult(winnerId, WasRejected: false);
            }

            var reject = winnerStatus == JobStatus.Failed
                ? duplicatePolicy is DuplicatePolicy.RejectIfFailed or DuplicatePolicy.RejectAlways
                : duplicatePolicy == DuplicatePolicy.RejectAlways;

            if (reject)
            {
                return new EnqueueResult(winnerId, WasRejected: true);
            }

            // Policy allows: return the winner's ID as the enqueued job
            return new EnqueueResult(winnerId, WasRejected: false);
        }
    }

    // ── FetchNextAsync ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<JobRecord?> FetchNextAsync(IReadOnlyList<string> queues, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Atomically promote any due scheduled/retry jobs first
        await PromoteDueScheduledJobsAsync(now, cancellationToken).ConfigureAwait(false);

        var filter = Builders<JobDocument>.Filter.And(
            Builders<JobDocument>.Filter.In(d => d.Queue, queues),
            Builders<JobDocument>.Filter.Eq(d => d.Status, JobStatus.Enqueued));

        var sort = Builders<JobDocument>.Sort
            .Ascending(d => d.Priority)   // Critical=1 first
            .Ascending(d => d.CreatedAt);

        var update = Builders<JobDocument>.Update
            .Set(d => d.Status, JobStatus.Processing)
            .Set(d => d.ProcessingStartedAt, now)
            .Set(d => d.HeartbeatAt, now)
            .Inc(d => d.Attempts, 1);

        var options = new FindOneAndUpdateOptions<JobDocument>
        {
            Sort = sort,
            ReturnDocument = ReturnDocument.After,
        };

        var doc = await _jobs.FindOneAndUpdateAsync(filter, update, options, cancellationToken).ConfigureAwait(false);
        return doc?.ToRecord();
    }

    // ── AcknowledgeAsync ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task AcknowledgeAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        var update = Builders<JobDocument>.Update
            .Set(d => d.Status, JobStatus.Succeeded)
            .Set(d => d.CompletedAt, DateTimeOffset.UtcNow)
            .Unset(d => d.HeartbeatAt);

        await _jobs.UpdateOneAsync(ById(jobId), update, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // ── SetFailedAsync ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task SetFailedAsync(JobId jobId, Exception exception, DateTimeOffset? retryAt, CancellationToken cancellationToken = default)
    {
        UpdateDefinition<JobDocument> update;

        if (retryAt.HasValue)
        {
            update = Builders<JobDocument>.Update
                .Set(d => d.Status, JobStatus.Scheduled)
                .Set(d => d.RetryAt, retryAt.Value)
                .Set(d => d.LastErrorMessage, exception.Message)
                .Set(d => d.LastErrorStackTrace, exception.StackTrace)
                .Unset(d => d.HeartbeatAt);
        }
        else
        {
            update = Builders<JobDocument>.Update
                .Set(d => d.Status, JobStatus.Failed)
                .Set(d => d.CompletedAt, DateTimeOffset.UtcNow)
                .Set(d => d.LastErrorMessage, exception.Message)
                .Set(d => d.LastErrorStackTrace, exception.StackTrace)
                .Unset(d => d.HeartbeatAt);
        }

        await _jobs.UpdateOneAsync(ById(jobId), update, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SetExpiredAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        var update = Builders<JobDocument>.Update
            .Set(d => d.Status, JobStatus.Expired)
            .Set(d => d.CompletedAt, DateTimeOffset.UtcNow)
            .Unset(d => d.HeartbeatAt);

        await _jobs.UpdateOneAsync(ById(jobId), update, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // ── UpdateHeartbeatAsync ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task UpdateHeartbeatAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        var update = Builders<JobDocument>.Update
            .Set(d => d.HeartbeatAt, DateTimeOffset.UtcNow);

        await _jobs.UpdateOneAsync(ById(jobId), update, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // ── Recurring jobs ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task UpsertRecurringJobAsync(RecurringJobRecord recurringJob, CancellationToken cancellationToken = default)
    {
        var filter = Builders<RecurringJobDocument>.Filter.Eq(d => d.RecurringJobId, recurringJob.RecurringJobId);

        // On insert set cron_override=null, enabled=true, deleted_by_user=false.
        // On update preserve existing values for those three user-controlled fields.
        var update = Builders<RecurringJobDocument>.Update
            .Set(d => d.JobType, recurringJob.JobType)
            .Set(d => d.InputType, recurringJob.InputType)
            .Set(d => d.InputJson, recurringJob.InputJson)
            .Set(d => d.Cron, recurringJob.Cron)
            .Set(d => d.TimeZoneId, recurringJob.TimeZoneId)
            .Set(d => d.Queue, recurringJob.Queue)
            .Set(d => d.NextExecution, recurringJob.NextExecution)
            .Set(d => d.CreatedAt, recurringJob.CreatedAt)
            .Set(d => d.ConcurrencyPolicy, recurringJob.ConcurrencyPolicy)
            .SetOnInsert(d => d.CronOverride, (string?)null)
            .SetOnInsert(d => d.Enabled, true)
            .SetOnInsert(d => d.DeletedByUser, false);

        var options = new UpdateOptions { IsUpsert = true };
        await _recurringJobs.UpdateOneAsync(filter, update, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RecurringJobRecord>> GetDueRecurringJobsAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        var filter = Builders<RecurringJobDocument>.Filter.Lte(d => d.NextExecution, utcNow);
        var docs = await _recurringJobs.Find(filter).ToListAsync(cancellationToken).ConfigureAwait(false);
        return docs.Select(d => d.ToRecord()).ToList();
    }

    /// <inheritdoc/>
    public async Task SetRecurringJobNextExecutionAsync(string recurringJobId, DateTimeOffset nextExecution, CancellationToken cancellationToken = default)
    {
        var filter = Builders<RecurringJobDocument>.Filter.Eq(d => d.RecurringJobId, recurringJobId);
        var update = Builders<RecurringJobDocument>.Update
            .Set(d => d.NextExecution, nextExecution)
            .Set(d => d.LastExecutedAt, DateTimeOffset.UtcNow);

        await _recurringJobs.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SetRecurringJobLastExecutionResultAsync(string recurringJobId, JobStatus status, string? errorMessage, CancellationToken cancellationToken = default)
    {
        var filter = Builders<RecurringJobDocument>.Filter.Eq(d => d.RecurringJobId, recurringJobId);
        var update = Builders<RecurringJobDocument>.Update
            .Set(d => d.LastExecutionStatus, (JobStatus?)status)
            .Set(d => d.LastExecutionError, errorMessage);

        await _recurringJobs.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteRecurringJobAsync(string recurringJobId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<RecurringJobDocument>.Filter.Eq(d => d.RecurringJobId, recurringJobId);
        await _recurringJobs.DeleteOneAsync(filter, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RecurringJobRecord>> GetRecurringJobsAsync(CancellationToken cancellationToken = default)
    {
        var docs = await _recurringJobs.Find(Builders<RecurringJobDocument>.Filter.Empty)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return docs.Select(d => d.ToRecord()).ToList();
    }

    /// <inheritdoc/>
    public async Task<RecurringJobRecord?> GetRecurringJobByIdAsync(string recurringJobId, CancellationToken cancellationToken = default)
    {
        return await GetRecurringJobAsync(recurringJobId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<RecurringJobRecord?> GetRecurringJobAsync(string recurringJobId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<RecurringJobDocument>.Filter.Eq(d => d.RecurringJobId, recurringJobId);
        var doc = await _recurringJobs.Find(filter).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return doc?.ToRecord();
    }

    /// <inheritdoc/>
    public async Task UpdateRecurringJobConfigAsync(
        string recurringJobId, string? cronOverride, bool enabled,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<RecurringJobDocument>.Filter.Eq(d => d.RecurringJobId, recurringJobId);
        var update = Builders<RecurringJobDocument>.Update
            .Set(d => d.CronOverride, cronOverride)
            .Set(d => d.Enabled, enabled);

        await _recurringJobs.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ForceDeleteRecurringJobAsync(
        string recurringJobId, CancellationToken cancellationToken = default)
    {
        var jobsFilter = Builders<JobDocument>.Filter.Eq(d => d.RecurringJobId, recurringJobId);
        await _jobs.DeleteManyAsync(jobsFilter, cancellationToken).ConfigureAwait(false);

        var recurringFilter = Builders<RecurringJobDocument>.Filter.Eq(d => d.RecurringJobId, recurringJobId);
        var update = Builders<RecurringJobDocument>.Update
            .Set(d => d.DeletedByUser, true)
            .Set(d => d.Enabled, false);
        await _recurringJobs.UpdateOneAsync(recurringFilter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RestoreRecurringJobAsync(
        string recurringJobId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<RecurringJobDocument>.Filter.Eq(d => d.RecurringJobId, recurringJobId);
        var update = Builders<RecurringJobDocument>.Update
            .Set(d => d.DeletedByUser, false)
            .Set(d => d.Enabled, true);
        await _recurringJobs.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // ── Orphan requeue ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task RequeueOrphanedJobsAsync(TimeSpan heartbeatTimeout, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - heartbeatTimeout;

        var filter = Builders<JobDocument>.Filter.And(
            Builders<JobDocument>.Filter.Eq(d => d.Status, JobStatus.Processing),
            Builders<JobDocument>.Filter.Lt(d => d.HeartbeatAt, cutoff));

        var update = Builders<JobDocument>.Update
            .Set(d => d.Status, JobStatus.Enqueued)
            .Unset(d => d.HeartbeatAt)
            .Unset(d => d.ProcessingStartedAt);

        await _jobs.UpdateManyAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // ── Continuations ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task EnqueueContinuationsAsync(JobId parentJobId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<JobDocument>.Filter.And(
            Builders<JobDocument>.Filter.Eq(d => d.Status, JobStatus.AwaitingContinuation),
            Builders<JobDocument>.Filter.Eq(d => d.ParentJobId, parentJobId));

        var update = Builders<JobDocument>.Update
            .Set(d => d.Status, JobStatus.Enqueued);

        await _jobs.UpdateManyAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // ── Server / Worker node tracking ─────────────────────────────────────────

    /// <inheritdoc/>
    public async Task RegisterServerAsync(ServerRecord server, CancellationToken cancellationToken = default)
    {
        var filter = Builders<ServerDocument>.Filter.Eq(d => d.Id, server.Id);

        var update = Builders<ServerDocument>.Update
            .Set(d => d.WorkerCount, server.WorkerCount)
            .Set(d => d.Queues, server.Queues)
            .Set(d => d.HeartbeatAt, server.HeartbeatAt)
            .SetOnInsert(d => d.StartedAt, server.StartedAt);

        var options = new UpdateOptions { IsUpsert = true };
        await _servers.UpdateOneAsync(filter, update, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task HeartbeatServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<ServerDocument>.Filter.Eq(d => d.Id, serverId);
        var update = Builders<ServerDocument>.Update.Set(d => d.HeartbeatAt, DateTimeOffset.UtcNow);
        await _servers.UpdateOneAsync(filter, update, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeregisterServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<ServerDocument>.Filter.Eq(d => d.Id, serverId);
        await _servers.DeleteOneAsync(filter, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ServerRecord>> GetActiveServersAsync(TimeSpan activeTimeout, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - activeTimeout;
        var filter = Builders<ServerDocument>.Filter.Gte(d => d.HeartbeatAt, cutoff);
        var sort = Builders<ServerDocument>.Sort.Ascending(d => d.Id);

        var docs = await _servers.Find(filter).Sort(sort).ToListAsync(cancellationToken).ConfigureAwait(false);
        return docs.Select(d => d.ToRecord()).ToList();
    }

    // ── Dashboard support ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<JobMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff24 = now.AddHours(-24);

        var statusCounts = await _jobs.Aggregate()
            .Group(d => d.Status, g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var byStatus = statusCounts.ToDictionary(x => x.Status, x => x.Count);

        var completed = await _jobs.Find(
                Builders<JobDocument>.Filter.And(
                    Builders<JobDocument>.Filter.In(d => d.Status, new[] { JobStatus.Succeeded, JobStatus.Failed }),
                    Builders<JobDocument>.Filter.Gte(d => d.CompletedAt, cutoff24)))
            .Project(d => new { d.CompletedAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var throughput = completed
            .Where(d => d.CompletedAt.HasValue)
            .GroupBy(d => new DateTimeOffset(
                d.CompletedAt!.Value.Year, d.CompletedAt.Value.Month, d.CompletedAt.Value.Day,
                d.CompletedAt.Value.Hour, 0, 0, TimeSpan.Zero))
            .Select(g => new HourlyThroughput { Hour = g.Key, Count = g.Count() })
            .OrderBy(h => h.Hour)
            .ToList();

        var recentFailures = (await _jobs
            .Find(Builders<JobDocument>.Filter.Eq(d => d.Status, JobStatus.Failed))
            .Sort(Builders<JobDocument>.Sort.Descending(d => d.CompletedAt))
            .Limit(10)
            .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(d => d.ToRecord())
            .ToList();

        var recurringCount = (int)await _recurringJobs.CountDocumentsAsync(
            FilterDefinition<RecurringJobDocument>.Empty, cancellationToken: cancellationToken).ConfigureAwait(false);

        return new JobMetrics
        {
            Enqueued = byStatus.GetValueOrDefault(JobStatus.Enqueued),
            Processing = byStatus.GetValueOrDefault(JobStatus.Processing),
            Succeeded = byStatus.GetValueOrDefault(JobStatus.Succeeded),
            Failed = byStatus.GetValueOrDefault(JobStatus.Failed),
            Scheduled = byStatus.GetValueOrDefault(JobStatus.Scheduled),
            Recurring = recurringCount,
            HourlyThroughput = throughput,
            RecentFailures = recentFailures,
        };
    }

    /// <inheritdoc/>
    public async Task<PagedResult<JobRecord>> GetJobsAsync(
        JobFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var fb = Builders<JobDocument>.Filter;
        var filterDef = fb.Empty;

        if (filter.Status.HasValue)
        {
            filterDef &= fb.Eq(d => d.Status, filter.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Queue))
        {
            filterDef &= fb.Eq(d => d.Queue, filter.Queue);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            filterDef &= fb.Or(
                fb.Regex(d => d.JobType, new BsonRegularExpression(term, "i")),
                fb.Regex(d => d.Id, new BsonRegularExpression(term, "i")));
        }

        if (!string.IsNullOrEmpty(filter.RecurringJobId))
        {
            filterDef &= fb.Eq(d => d.RecurringJobId, filter.RecurringJobId);
        }

        var total = (int)await _jobs.CountDocumentsAsync(filterDef, cancellationToken: cancellationToken).ConfigureAwait(false);

        var docs = await _jobs.Find(filterDef)
            .Sort(Builders<JobDocument>.Sort.Descending(d => d.CreatedAt))
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new PagedResult<JobRecord>
        {
            Items = docs.Select(d => d.ToRecord()).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <inheritdoc/>
    public async Task<JobRecord?> GetJobByIdAsync(JobId id, CancellationToken cancellationToken = default)
    {
        var doc = await _jobs.Find(ById(id)).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return doc?.ToRecord();
    }

    /// <inheritdoc/>
    public async Task DeleteJobAsync(JobId id, CancellationToken cancellationToken = default) =>
        await _jobs.DeleteOneAsync(ById(id), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task RequeueJobAsync(JobId id, CancellationToken cancellationToken = default)
    {
        var update = Builders<JobDocument>.Update
            .Set(d => d.Status, JobStatus.Enqueued)
            .Set(d => d.Attempts, 0)
            .Unset(d => d.RetryAt)
            .Unset(d => d.CompletedAt)
            .Unset(d => d.LastErrorMessage)
            .Unset(d => d.LastErrorStackTrace);

        await _jobs.UpdateOneAsync(ById(id), update, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<QueueMetrics>> GetQueueMetricsAsync(CancellationToken cancellationToken = default)
    {
        var pipeline = _jobs.Aggregate()
            .Match(Builders<JobDocument>.Filter.In(d => d.Status,
                new[] { JobStatus.Enqueued, JobStatus.Processing }))
            .Group(d => d.Queue, g => new
            {
                Queue = g.Key,
                Enqueued = g.Sum(x => x.Status == JobStatus.Enqueued ? 1 : 0),
                Processing = g.Sum(x => x.Status == JobStatus.Processing ? 1 : 0),
            });

        var results = await pipeline.ToListAsync(cancellationToken).ConfigureAwait(false);
        return results
            .Select(r => new QueueMetrics { Queue = r.Queue, Enqueued = r.Enqueued, Processing = r.Processing })
            .OrderBy(q => q.Queue, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task SaveExecutionLogsAsync(
        JobId jobId, IReadOnlyList<JobExecutionLog> logs,
        CancellationToken cancellationToken = default)
    {
        var entries = logs.Select(e => new ExecutionLogEntry
        {
            Timestamp = e.Timestamp,
            Level = e.Level,
            Message = e.Message,
        }).ToList();

        var update = Builders<JobDocument>.Update
            .Set(d => d.ExecutionLogs, entries);

        await _jobs.UpdateOneAsync(ById(jobId), update, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task CommitJobResultAsync(
        JobId jobId, JobExecutionResult result, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var entries = result.Logs.Select(e => new ExecutionLogEntry
        {
            Timestamp = e.Timestamp,
            Level = e.Level,
            Message = e.Message,
        }).ToList();

        // Idempotency guard: check if job is already in terminal state
        var currentJob = await _jobs.Find(ById(jobId)).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (currentJob is null || currentJob.Status is JobStatus.Succeeded or JobStatus.Failed or JobStatus.Expired)
        {
            return; // Already terminal, idempotent no-op
        }

        if (result.Succeeded)
        {
            var update = Builders<JobDocument>.Update
                .Set(d => d.Status, JobStatus.Succeeded)
                .Set(d => d.CompletedAt, now)
                .Unset(d => d.HeartbeatAt)
                .Set(d => d.ExecutionLogs, entries);

            await _jobs.UpdateOneAsync(ById(jobId), update, cancellationToken: cancellationToken).ConfigureAwait(false);

            // Enqueue continuations
            var contFilter = Builders<JobDocument>.Filter.And(
                Builders<JobDocument>.Filter.Eq(d => d.Status, JobStatus.AwaitingContinuation),
                Builders<JobDocument>.Filter.Eq(d => d.ParentJobId, jobId));

            var contUpdate = Builders<JobDocument>.Update
                .Set(d => d.Status, JobStatus.Enqueued)
                .Unset(d => d.ScheduledAt);

            await _jobs.UpdateManyAsync(contFilter, contUpdate, cancellationToken: cancellationToken).ConfigureAwait(false);

            // Update recurring result
            if (result.RecurringJobId is not null)
            {
                var recurFilter = Builders<RecurringJobDocument>.Filter.Eq(d => d.RecurringJobId, result.RecurringJobId);
                var recurUpdate = Builders<RecurringJobDocument>.Update
                    .Set(d => d.LastExecutionStatus, JobStatus.Succeeded)
                    .Unset(d => d.LastExecutionError);

                await _recurringJobs.UpdateOneAsync(recurFilter, recurUpdate, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            UpdateDefinition<JobDocument> jobUpdate;

            if (result.RetryAt.HasValue)
            {
                jobUpdate = Builders<JobDocument>.Update
                    .Set(d => d.Status, JobStatus.Scheduled)
                    .Set(d => d.RetryAt, result.RetryAt.Value)
                    .Set(d => d.LastErrorMessage, result.Exception?.Message)
                    .Set(d => d.LastErrorStackTrace, result.Exception?.StackTrace)
                    .Unset(d => d.HeartbeatAt)
                    .Set(d => d.ExecutionLogs, entries);
            }
            else
            {
                jobUpdate = Builders<JobDocument>.Update
                    .Set(d => d.Status, JobStatus.Failed)
                    .Set(d => d.CompletedAt, now)
                    .Set(d => d.LastErrorMessage, result.Exception?.Message)
                    .Set(d => d.LastErrorStackTrace, result.Exception?.StackTrace)
                    .Unset(d => d.HeartbeatAt)
                    .Set(d => d.ExecutionLogs, entries);
            }

            await _jobs.UpdateOneAsync(ById(jobId), jobUpdate, cancellationToken: cancellationToken).ConfigureAwait(false);

            // Update recurring result (only on dead-letter)
            if (result.RecurringJobId is not null && !result.RetryAt.HasValue)
            {
                var recurFilter = Builders<RecurringJobDocument>.Filter.Eq(d => d.RecurringJobId, result.RecurringJobId);
                var recurUpdate = Builders<RecurringJobDocument>.Update
                    .Set(d => d.LastExecutionStatus, JobStatus.Failed)
                    .Set(d => d.LastExecutionError, result.Exception?.Message);

                await _recurringJobs.UpdateOneAsync(recurFilter, recurUpdate, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TryAcquireRecurringJobLockAsync(
        string recurringJobId, TimeSpan ttl, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var expiry = now.Add(ttl);

        // Delete expired lock first
        await _recurringLocks.DeleteOneAsync(
            Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id", recurringJobId),
                Builders<BsonDocument>.Filter.Lt("expires_at", now)),
            ct).ConfigureAwait(false);

        try
        {
            await _recurringLocks.InsertOneAsync(
                new BsonDocument { ["_id"] = recurringJobId, ["expires_at"] = expiry },
                cancellationToken: ct).ConfigureAwait(false);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task ReleaseRecurringJobLockAsync(
        string recurringJobId, CancellationToken ct = default)
    {
        await _recurringLocks.DeleteOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", recurringJobId), ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ReportProgressAsync(
        JobId jobId, int percent, string? message, CancellationToken ct = default)
    {
        var update = Builders<JobDocument>.Update
            .Set(d => d.ProgressPercent, percent)
            .Set(d => d.ProgressMessage, message);
        await _jobs.UpdateOneAsync(ById(jobId), update, cancellationToken: ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<JobRecord>> GetJobsByTagAsync(
        string tag, CancellationToken cancellationToken = default)
    {
        var filter = Builders<JobDocument>.Filter.AnyEq(d => d.Tags, tag);
        var docs = await _jobs.Find(filter).ToListAsync(cancellationToken).ConfigureAwait(false);
        return docs.Select(d => d.ToRecord()).ToList();
    }

    /// <inheritdoc/>
    public async Task<int> PurgeJobsAsync(RetentionPolicy policy, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var deleted = 0;

        if (policy.RetainSucceeded > TimeSpan.Zero)
        {
            var cutoff = now - policy.RetainSucceeded;
            var result = await _jobs.DeleteManyAsync(
                Builders<JobDocument>.Filter.And(
                    Builders<JobDocument>.Filter.Eq(j => j.Status, JobStatus.Succeeded),
                    Builders<JobDocument>.Filter.Lt(j => j.CompletedAt, cutoff)),
                cancellationToken).ConfigureAwait(false);
            deleted += (int)result.DeletedCount;
        }

        if (policy.RetainFailed > TimeSpan.Zero)
        {
            var cutoff = now - policy.RetainFailed;
            var result = await _jobs.DeleteManyAsync(
                Builders<JobDocument>.Filter.And(
                    Builders<JobDocument>.Filter.Eq(j => j.Status, JobStatus.Failed),
                    Builders<JobDocument>.Filter.Lt(j => j.CompletedAt, cutoff)),
                cancellationToken).ConfigureAwait(false);
            deleted += (int)result.DeletedCount;
        }

        if (policy.RetainExpired > TimeSpan.Zero)
        {
            var cutoff = now - policy.RetainExpired;
            var result = await _jobs.DeleteManyAsync(
                Builders<JobDocument>.Filter.And(
                    Builders<JobDocument>.Filter.Eq(j => j.Status, JobStatus.Expired),
                    Builders<JobDocument>.Filter.Lt(j => j.CreatedAt, cutoff)),
                cancellationToken).ConfigureAwait(false);
            deleted += (int)result.DeletedCount;
        }

        return deleted;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool IsActiveState(JobStatus status) =>
        status is JobStatus.Enqueued or JobStatus.Processing or JobStatus.Scheduled or JobStatus.AwaitingContinuation;

    private static FilterDefinition<JobDocument> ById(JobId id) =>
        Builders<JobDocument>.Filter.Eq(d => d.Id, id);

    private async Task PromoteDueScheduledJobsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var filter = Builders<JobDocument>.Filter.And(
            Builders<JobDocument>.Filter.Eq(d => d.Status, JobStatus.Scheduled),
            Builders<JobDocument>.Filter.Or(
                // retry scheduled: RetryAt is set and due
                Builders<JobDocument>.Filter.Lte(d => d.RetryAt, now),
                // first-time scheduled: RetryAt is null and ScheduledAt is due
                Builders<JobDocument>.Filter.And(
                    Builders<JobDocument>.Filter.Eq(d => d.RetryAt, (DateTimeOffset?)null),
                    Builders<JobDocument>.Filter.Lte(d => d.ScheduledAt, now))));

        var update = Builders<JobDocument>.Update
            .Set(d => d.Status, JobStatus.Enqueued);

        await _jobs.UpdateManyAsync(filter, update, cancellationToken: ct).ConfigureAwait(false);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private void EnsureIndexes()
    {
        // Compound index for FetchNextAsync
        _jobs.Indexes.CreateOne(new CreateIndexModel<JobDocument>(
            Builders<JobDocument>.IndexKeys
                .Ascending(d => d.Queue)
                .Ascending(d => d.Status)
                .Ascending(d => d.Priority)
                .Ascending(d => d.CreatedAt),
            new CreateIndexOptions { Name = "queue_status_priority_created" }));

        // Sparse index for idempotency: efficient querying, but doesn't prevent race conditions.
        // Race conditions handled at application level via try-catch + DuplicateKey detection (see EnqueueAsync).
        // Note: MongoDB with null IdempotencyKey values means the field is null in BSON, not missing.
        // So even Sparse=true will include documents with IdempotencyKey:null, and unique constraint
        // would block multiple nulls. Application-level handling avoids this MongoDB semantic issue.
        _jobs.Indexes.CreateOne(new CreateIndexModel<JobDocument>(
            Builders<JobDocument>.IndexKeys.Ascending(d => d.IdempotencyKey),
            new CreateIndexOptions { Name = "idempotency_key", Sparse = true }));

        // Index for orphan detection
        _jobs.Indexes.CreateOne(new CreateIndexModel<JobDocument>(
            Builders<JobDocument>.IndexKeys
                .Ascending(d => d.Status)
                .Ascending(d => d.HeartbeatAt),
            new CreateIndexOptions { Name = "status_heartbeat" }));

        // Index for continuation activation
        _jobs.Indexes.CreateOne(new CreateIndexModel<JobDocument>(
            Builders<JobDocument>.IndexKeys.Ascending(d => d.ParentJobId),
            new CreateIndexOptions { Name = "parent_job_id", Sparse = true }));

        // Index for scheduled-job promotion
        _jobs.Indexes.CreateOne(new CreateIndexModel<JobDocument>(
            Builders<JobDocument>.IndexKeys
                .Ascending(d => d.Status)
                .Ascending(d => d.ScheduledAt)
                .Ascending(d => d.RetryAt),
            new CreateIndexOptions { Name = "status_scheduled_retry" }));

        // Recurring jobs: due-date index
        _recurringJobs.Indexes.CreateOne(new CreateIndexModel<RecurringJobDocument>(
            Builders<RecurringJobDocument>.IndexKeys.Ascending(d => d.NextExecution),
            new CreateIndexOptions { Name = "next_execution" }));

        // RecurringJobId is [BsonId] — MongoDB already enforces unique on _id.

        // TTL index on recurring locks — MongoDB removes expired documents automatically
        _recurringLocks.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Ascending("expires_at"),
            new CreateIndexOptions { ExpireAfter = TimeSpan.Zero }));

        // TTL index for orphaned servers (expires 1 hour after last heartbeat)
        _servers.Indexes.CreateOne(new CreateIndexModel<ServerDocument>(
            Builders<ServerDocument>.IndexKeys.Ascending(d => d.HeartbeatAt),
            new CreateIndexOptions { Name = "heartbeat_ttl", ExpireAfter = TimeSpan.FromHours(1) }));
    }
}
