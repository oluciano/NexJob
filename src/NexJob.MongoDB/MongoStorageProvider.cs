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

    static MongoStorageProvider()
    {
        // Register enum-as-string convention globally for NexJob documents
        var pack = new ConventionPack { new EnumRepresentationConvention(BsonType.String) };
        ConventionRegistry.Register("NexJobEnumAsString", pack, t =>
            t == typeof(JobDocument) || t == typeof(RecurringJobDocument));

        // Store DateTimeOffset as a UTC DateTime tick pair to preserve offset
        BsonSerializer.TryRegisterSerializer(new DateTimeOffsetSerializer(BsonType.String));
    }

    /// <summary>
    /// Initialises the provider using an existing <see cref="IMongoDatabase"/>.
    /// Indexes are created on construction (idempotent — safe to call multiple times).
    /// </summary>
    public MongoStorageProvider(IMongoDatabase database)
    {
        _jobs          = database.GetCollection<JobDocument>("nexjob_jobs");
        _recurringJobs = database.GetCollection<RecurringJobDocument>("nexjob_recurring_jobs");

        EnsureIndexes();
    }

    // ── EnqueueAsync ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<JobId> EnqueueAsync(JobRecord job, CancellationToken cancellationToken = default)
    {
        // Idempotency check — return existing job if key already active
        if (job.IdempotencyKey is not null)
        {
            var existing = await _jobs.Find(
                Builders<JobDocument>.Filter.And(
                    Builders<JobDocument>.Filter.Eq(d => d.IdempotencyKey, job.IdempotencyKey),
                    Builders<JobDocument>.Filter.In(d => d.Status, new[] { JobStatus.Enqueued, JobStatus.Processing, JobStatus.Scheduled, JobStatus.AwaitingContinuation })
                )).FirstOrDefaultAsync(cancellationToken);

            if (existing is not null)
                return existing.Id;
        }

        await _jobs.InsertOneAsync(JobDocument.FromRecord(job), cancellationToken: cancellationToken);
        return job.Id;
    }

    // ── FetchNextAsync ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<JobRecord?> FetchNextAsync(IReadOnlyList<string> queues, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        // Atomically promote any due scheduled/retry jobs first
        await PromoteDueScheduledJobsAsync(now, cancellationToken);

        var filter = Builders<JobDocument>.Filter.And(
            Builders<JobDocument>.Filter.In(d => d.Queue, queues),
            Builders<JobDocument>.Filter.Eq(d => d.Status, JobStatus.Enqueued)
        );

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
            Sort           = sort,
            ReturnDocument = ReturnDocument.After,
        };

        var doc = await _jobs.FindOneAndUpdateAsync(filter, update, options, cancellationToken);
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

        await _jobs.UpdateOneAsync(ById(jobId), update, cancellationToken: cancellationToken);
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

        await _jobs.UpdateOneAsync(ById(jobId), update, cancellationToken: cancellationToken);
    }

    // ── UpdateHeartbeatAsync ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task UpdateHeartbeatAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        var update = Builders<JobDocument>.Update
            .Set(d => d.HeartbeatAt, DateTimeOffset.UtcNow);

        await _jobs.UpdateOneAsync(ById(jobId), update, cancellationToken: cancellationToken);
    }

    // ── Recurring jobs ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task UpsertRecurringJobAsync(RecurringJobRecord recurringJob, CancellationToken cancellationToken = default)
    {
        var doc    = RecurringJobDocument.FromRecord(recurringJob);
        var filter = Builders<RecurringJobDocument>.Filter.Eq(d => d.RecurringJobId, recurringJob.RecurringJobId);
        var options = new ReplaceOptions { IsUpsert = true };

        await _recurringJobs.ReplaceOneAsync(filter, doc, options, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RecurringJobRecord>> GetDueRecurringJobsAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        var filter = Builders<RecurringJobDocument>.Filter.Lte(d => d.NextExecution, utcNow);
        var docs   = await _recurringJobs.Find(filter).ToListAsync(cancellationToken);
        return docs.Select(d => d.ToRecord()).ToList();
    }

    /// <inheritdoc/>
    public async Task SetRecurringJobNextExecutionAsync(string recurringJobId, DateTimeOffset nextExecution, CancellationToken cancellationToken = default)
    {
        var filter = Builders<RecurringJobDocument>.Filter.Eq(d => d.RecurringJobId, recurringJobId);
        var update = Builders<RecurringJobDocument>.Update
            .Set(d => d.NextExecution, nextExecution)
            .Set(d => d.LastExecutedAt, DateTimeOffset.UtcNow);

        await _recurringJobs.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteRecurringJobAsync(string recurringJobId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<RecurringJobDocument>.Filter.Eq(d => d.RecurringJobId, recurringJobId);
        await _recurringJobs.DeleteOneAsync(filter, cancellationToken);
    }

    // ── Orphan requeue ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task RequeueOrphanedJobsAsync(TimeSpan heartbeatTimeout, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - heartbeatTimeout;

        var filter = Builders<JobDocument>.Filter.And(
            Builders<JobDocument>.Filter.Eq(d => d.Status, JobStatus.Processing),
            Builders<JobDocument>.Filter.Lt(d => d.HeartbeatAt, cutoff)
        );

        var update = Builders<JobDocument>.Update
            .Set(d => d.Status, JobStatus.Enqueued)
            .Unset(d => d.HeartbeatAt)
            .Unset(d => d.ProcessingStartedAt);

        await _jobs.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);
    }

    // ── Continuations ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task EnqueueContinuationsAsync(JobId parentJobId, CancellationToken cancellationToken = default)
    {
        var filter = Builders<JobDocument>.Filter.And(
            Builders<JobDocument>.Filter.Eq(d => d.Status, JobStatus.AwaitingContinuation),
            Builders<JobDocument>.Filter.Eq(d => d.ParentJobId, parentJobId)
        );

        var update = Builders<JobDocument>.Update
            .Set(d => d.Status, JobStatus.Enqueued);

        await _jobs.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

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
                    Builders<JobDocument>.Filter.Lte(d => d.ScheduledAt, now)
                )
            )
        );

        var update = Builders<JobDocument>.Update
            .Set(d => d.Status, JobStatus.Enqueued);

        await _jobs.UpdateManyAsync(filter, update, cancellationToken: ct);
    }

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

        // Sparse unique index for idempotency
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
    }
}
