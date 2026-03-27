using System.Data;
using System.Text.Json;
using Dapper;
using NexJob.Storage;
using Npgsql;

namespace NexJob.Postgres;

/// <summary>
/// PostgreSQL-backed implementation of <see cref="IStorageProvider"/>.
/// Uses <c>SELECT FOR UPDATE SKIP LOCKED</c> for atomic job claiming, preventing
/// double-processing across multiple workers or server instances.
/// </summary>
public sealed class PostgresStorageProvider : IStorageProvider
{
    private readonly string _connectionString;

    /// <summary>
    /// Initialises the provider and applies all pending schema migrations.
    /// Acquires a PostgreSQL advisory lock so only one instance migrates at a time.
    /// </summary>
    public PostgresStorageProvider(string connectionString)
    {
        _connectionString = connectionString;
        // Allow Dapper to match snake_case column names to PascalCase properties
        // (e.g., recurring_job_id → RecurringJobId, completed_at → CompletedAt)
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        // Sync-over-async is acceptable here: runs once at startup, before any requests are served.
        new SchemaMigrator().MigrateAsync(connectionString).GetAwaiter().GetResult();
    }

    // ── EnqueueAsync ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<JobId> EnqueueAsync(JobRecord job, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);

        if (job.IdempotencyKey is not null)
        {
            var existing = await conn.QuerySingleOrDefaultAsync<Guid?>(
                """
                SELECT id FROM nexjob_jobs
                WHERE idempotency_key = @key
                  AND status IN ('Enqueued','Processing','Scheduled','AwaitingContinuation')
                LIMIT 1
                """,
                new { key = job.IdempotencyKey });

            if (existing.HasValue)
            {
                return new JobId(existing.Value);
            }
        }

        await conn.ExecuteAsync(
            """
            INSERT INTO nexjob_jobs
                (id, job_type, input_type, input_json, schema_version, queue, priority, status,
                 idempotency_key, attempts, max_attempts, created_at, scheduled_at, parent_job_id, recurring_job_id, tags)
            VALUES
                (@Id, @JobType, @InputType, @InputJson::jsonb, @SchemaVersion, @Queue, @Priority,
                 @Status, @IdempotencyKey, @Attempts, @MaxAttempts, @CreatedAt, @ScheduledAt, @ParentJobId, @RecurringJobId, @Tags)
            """,
            new
            {
                Id = job.Id.Value,
                job.JobType,
                job.InputType,
                job.InputJson,
                job.SchemaVersion,
                job.Queue,
                Priority = (int)job.Priority,
                Status = job.Status.ToString(),
                job.IdempotencyKey,
                job.Attempts,
                job.MaxAttempts,
                job.CreatedAt,
                job.ScheduledAt,
                ParentJobId = job.ParentJobId?.Value,
                job.RecurringJobId,
                Tags = job.Tags.ToArray(),
            });

        return job.Id;
    }

    // ── FetchNextAsync ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<JobRecord?> FetchNextAsync(
        IReadOnlyList<string> queues, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        // Promote due scheduled/retry jobs first
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_jobs
            SET status = 'Enqueued'
            WHERE status = 'Scheduled'
              AND (
                    (retry_at IS NOT NULL AND retry_at <= NOW())
                 OR (retry_at IS NULL AND scheduled_at IS NOT NULL AND scheduled_at <= NOW())
              )
            """, transaction: tx);

        var row = await conn.QuerySingleOrDefaultAsync<JobRow>(
            """
            UPDATE nexjob_jobs
            SET status                = 'Processing',
                processing_started_at = NOW(),
                heartbeat_at          = NOW(),
                attempts              = attempts + 1
            WHERE id = (
                SELECT id FROM nexjob_jobs
                WHERE status = 'Enqueued'
                  AND queue = ANY(@queues)
                ORDER BY
                    array_position(@queues, queue),
                    priority ASC,
                    created_at ASC
                LIMIT 1
                FOR UPDATE SKIP LOCKED
            )
            RETURNING *
            """,
            new { queues = queues.ToArray() },
            transaction: tx);

        await tx.CommitAsync(cancellationToken);
        return row?.ToRecord();
    }

    // ── AcknowledgeAsync ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task AcknowledgeAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_jobs
            SET status = 'Succeeded', completed_at = NOW(), heartbeat_at = NULL
            WHERE id = @id
            """,
            new { id = jobId.Value });
    }

    // ── SetFailedAsync ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task SetFailedAsync(
        JobId jobId, Exception exception, DateTimeOffset? retryAt,
        CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);

        if (retryAt.HasValue)
        {
            await conn.ExecuteAsync(
                """
                UPDATE nexjob_jobs
                SET status = 'Scheduled', retry_at = @retryAt,
                    exception_message = @msg, exception_stack_trace = @stack,
                    heartbeat_at = NULL
                WHERE id = @id
                """,
                new { id = jobId.Value, retryAt = retryAt.Value, msg = exception.Message, stack = exception.StackTrace });
        }
        else
        {
            await conn.ExecuteAsync(
                """
                UPDATE nexjob_jobs
                SET status = 'Failed', completed_at = NOW(),
                    exception_message = @msg, exception_stack_trace = @stack,
                    heartbeat_at = NULL
                WHERE id = @id
                """,
                new { id = jobId.Value, msg = exception.Message, stack = exception.StackTrace });
        }
    }

    // ── UpdateHeartbeatAsync ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task UpdateHeartbeatAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await conn.ExecuteAsync(
            "UPDATE nexjob_jobs SET heartbeat_at = NOW() WHERE id = @id",
            new { id = jobId.Value });
    }

    // ── Recurring jobs ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task UpsertRecurringJobAsync(
        RecurringJobRecord recurringJob, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await conn.ExecuteAsync(
            """
            INSERT INTO nexjob_recurring_jobs
                (recurring_job_id, job_type, input_type, input_json, cron,
                 time_zone_id, queue, next_execution, concurrency_policy, created_at, updated_at,
                 cron_override, enabled)
            VALUES
                (@RecurringJobId, @JobType, @InputType, @InputJson::jsonb, @Cron,
                 @TimeZoneId, @Queue, @NextExecution, @ConcurrencyPolicy, @CreatedAt, NOW(),
                 NULL, TRUE)
            ON CONFLICT (recurring_job_id) DO UPDATE
            SET job_type           = EXCLUDED.job_type,
                input_type         = EXCLUDED.input_type,
                input_json         = EXCLUDED.input_json,
                cron               = EXCLUDED.cron,
                time_zone_id       = EXCLUDED.time_zone_id,
                queue              = EXCLUDED.queue,
                next_execution     = EXCLUDED.next_execution,
                concurrency_policy = EXCLUDED.concurrency_policy,
                updated_at         = NOW()
            -- cron_override, enabled, and deleted_by_user are intentionally excluded:
            -- they are user-controlled and must not be overwritten by application startup
            """,
            new
            {
                recurringJob.RecurringJobId,
                recurringJob.JobType,
                recurringJob.InputType,
                recurringJob.InputJson,
                recurringJob.Cron,
                TimeZoneId = recurringJob.TimeZoneId ?? "UTC",
                recurringJob.Queue,
                recurringJob.NextExecution,
                recurringJob.CreatedAt,
                ConcurrencyPolicy = recurringJob.ConcurrencyPolicy.ToString(),
            });
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RecurringJobRecord>> GetDueRecurringJobsAsync(
        DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        var rows = await conn.QueryAsync<RecurringJobRow>(
            "SELECT * FROM nexjob_recurring_jobs WHERE next_execution <= @now",
            new { now = utcNow });
        return rows.Select(r => r.ToRecord()).ToList();
    }

    /// <inheritdoc/>
    public async Task SetRecurringJobNextExecutionAsync(
        string recurringJobId, DateTimeOffset nextExecution,
        CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_recurring_jobs
            SET next_execution = @next, last_execution = NOW(), updated_at = NOW()
            WHERE recurring_job_id = @id
            """,
            new { id = recurringJobId, next = nextExecution });
    }

    /// <inheritdoc/>
    public async Task SetRecurringJobLastExecutionResultAsync(
        string recurringJobId, JobStatus status, string? errorMessage,
        CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_recurring_jobs
            SET last_execution_status = @status, last_execution_error = @error, updated_at = NOW()
            WHERE recurring_job_id = @id
            """,
            new { id = recurringJobId, status = status.ToString(), error = errorMessage });
    }

    /// <inheritdoc/>
    public async Task DeleteRecurringJobAsync(
        string recurringJobId, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await conn.ExecuteAsync(
            "DELETE FROM nexjob_recurring_jobs WHERE recurring_job_id = @id",
            new { id = recurringJobId });
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RecurringJobRecord>> GetRecurringJobsAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        var rows = await conn.QueryAsync<RecurringJobRow>(
            "SELECT * FROM nexjob_recurring_jobs ORDER BY recurring_job_id");
        return rows.Select(r => r.ToRecord()).ToList();
    }

    /// <inheritdoc/>
    public async Task<RecurringJobRecord?> GetRecurringJobByIdAsync(string recurringJobId, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        var row = await conn.QuerySingleOrDefaultAsync<RecurringJobRow>(
            "SELECT * FROM nexjob_recurring_jobs WHERE recurring_job_id = @Id",
            new { Id = recurringJobId });
        return row?.ToRecord();
    }

    /// <inheritdoc/>
    public async Task UpdateRecurringJobConfigAsync(
        string recurringJobId, string? cronOverride, bool enabled,
        CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_recurring_jobs
            SET cron_override = @CronOverride, enabled = @Enabled, updated_at = NOW()
            WHERE recurring_job_id = @Id
            """,
            new { Id = recurringJobId, CronOverride = cronOverride, Enabled = enabled });
    }

    /// <inheritdoc/>
    public async Task ForceDeleteRecurringJobAsync(
        string recurringJobId, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await conn.ExecuteAsync(
            "DELETE FROM nexjob_jobs WHERE recurring_job_id = @id",
            new { id = recurringJobId });
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_recurring_jobs
            SET deleted_by_user = TRUE, enabled = FALSE, updated_at = NOW()
            WHERE recurring_job_id = @id
            """,
            new { id = recurringJobId });
    }

    /// <inheritdoc/>
    public async Task RestoreRecurringJobAsync(
        string recurringJobId, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_recurring_jobs
            SET deleted_by_user = FALSE, enabled = TRUE, updated_at = NOW()
            WHERE recurring_job_id = @id
            """,
            new { id = recurringJobId });
    }

    // ── Orphan requeue ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task RequeueOrphanedJobsAsync(
        TimeSpan heartbeatTimeout, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - heartbeatTimeout;
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_jobs
            SET status = 'Enqueued', heartbeat_at = NULL, processing_started_at = NULL
            WHERE status = 'Processing' AND heartbeat_at < @cutoff
            """,
            new { cutoff });
    }

    // ── Continuations ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task EnqueueContinuationsAsync(
        JobId parentJobId, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_jobs
            SET status = 'Enqueued'
            WHERE status = 'AwaitingContinuation' AND parent_job_id = @parentId
            """,
            new { parentId = parentJobId.Value });
    }

    // ── Dashboard support ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<JobMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);

        var counts = (await conn.QueryAsync<(string Status, int Count)>(
            "SELECT status, COUNT(*)::int FROM nexjob_jobs GROUP BY status"))
            .ToDictionary(x => x.Status, x => x.Count);

        var throughput = (await conn.QueryAsync<(DateTimeOffset Hour, int Count)>(
            """
            SELECT date_trunc('hour', completed_at) AS hour, COUNT(*)::int
            FROM nexjob_jobs
            WHERE completed_at >= NOW() - INTERVAL '24 hours'
              AND status IN ('Succeeded','Failed')
            GROUP BY hour
            ORDER BY hour
            """))
            .Select(r => new HourlyThroughput { Hour = r.Hour, Count = r.Count })
            .ToList();

        var recentFailures = (await conn.QueryAsync<JobRow>(
            "SELECT * FROM nexjob_jobs WHERE status = 'Failed' ORDER BY completed_at DESC LIMIT 10"))
            .Select(r => r.ToRecord())
            .ToList();

        var recurringCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM nexjob_recurring_jobs");

        return new JobMetrics
        {
            Enqueued = counts.GetValueOrDefault("Enqueued"),
            Processing = counts.GetValueOrDefault("Processing"),
            Succeeded = counts.GetValueOrDefault("Succeeded"),
            Failed = counts.GetValueOrDefault("Failed"),
            Scheduled = counts.GetValueOrDefault("Scheduled"),
            Recurring = recurringCount,
            HourlyThroughput = throughput,
            RecentFailures = recentFailures,
        };
    }

    /// <inheritdoc/>
    public async Task<PagedResult<JobRecord>> GetJobsAsync(
        JobFilter filter, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);

        var where = new List<string>();
        var p = new DynamicParameters();

        if (filter.Status.HasValue) { where.Add("status = @status"); p.Add("status", filter.Status.Value.ToString()); }
        if (!string.IsNullOrWhiteSpace(filter.Queue)) { where.Add("queue = @queue"); p.Add("queue", filter.Queue); }
        if (!string.IsNullOrWhiteSpace(filter.Search)) { where.Add("(job_type ILIKE @search OR id::text ILIKE @search)"); p.Add("search", $"%{filter.Search.Trim()}%"); }
        if (!string.IsNullOrEmpty(filter.RecurringJobId)) { where.Add("recurring_job_id = @recurringJobId"); p.Add("recurringJobId", filter.RecurringJobId); }

        var clause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty;
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*)::int FROM nexjob_jobs {clause}", p);

        p.Add("limit", pageSize);
        p.Add("offset", (page - 1) * pageSize);

        var items = (await conn.QueryAsync<JobRow>(
            $"SELECT * FROM nexjob_jobs {clause} ORDER BY created_at DESC LIMIT @limit OFFSET @offset", p))
            .Select(r => r.ToRecord())
            .ToList();

        return new PagedResult<JobRecord> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }

    /// <inheritdoc/>
    public async Task<JobRecord?> GetJobByIdAsync(JobId id, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        var row = await conn.QuerySingleOrDefaultAsync<JobRow>(
            "SELECT * FROM nexjob_jobs WHERE id = @id", new { id = id.Value });
        return row?.ToRecord();
    }

    /// <inheritdoc/>
    public async Task DeleteJobAsync(JobId id, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await conn.ExecuteAsync("DELETE FROM nexjob_jobs WHERE id = @id", new { id = id.Value });
    }

    /// <inheritdoc/>
    public async Task RequeueJobAsync(JobId id, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_jobs
            SET status = 'Enqueued', attempts = 0, retry_at = NULL,
                completed_at = NULL, exception_message = NULL, exception_stack_trace = NULL
            WHERE id = @id
            """,
            new { id = id.Value });
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<QueueMetrics>> GetQueueMetricsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        var rows = await conn.QueryAsync<(string Queue, int Enqueued, int Processing)>(
            """
            SELECT queue,
                   COUNT(*) FILTER (WHERE status = 'Enqueued')::int   AS enqueued,
                   COUNT(*) FILTER (WHERE status = 'Processing')::int AS processing
            FROM nexjob_jobs
            WHERE status IN ('Enqueued','Processing')
            GROUP BY queue
            ORDER BY queue
            """);

        return rows
            .Select(r => new QueueMetrics { Queue = r.Queue, Enqueued = r.Enqueued, Processing = r.Processing })
            .ToList();
    }

    /// <inheritdoc/>
    public async Task SaveExecutionLogsAsync(
        JobId jobId, IReadOnlyList<JobExecutionLog> logs,
        CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await conn.ExecuteAsync(
            "UPDATE nexjob_jobs SET execution_logs = @Logs::jsonb WHERE id = @Id",
            new { Id = jobId.Value, Logs = JsonSerializer.Serialize(logs) });
    }

    /// <inheritdoc/>
    public async Task<bool> TryAcquireRecurringJobLockAsync(
        string recurringJobId, TimeSpan ttl, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(
            "DELETE FROM nexjob_recurring_locks WHERE expires_at < NOW()");
        var rows = await conn.ExecuteAsync(
            """
            INSERT INTO nexjob_recurring_locks (recurring_job_id, expires_at)
            VALUES (@id, NOW() + @ttl::interval)
            ON CONFLICT (recurring_job_id) DO NOTHING
            """,
            new { id = recurringJobId, ttl = ttl.ToString() });
        return rows > 0;
    }

    /// <inheritdoc/>
    public async Task ReleaseRecurringJobLockAsync(
        string recurringJobId, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(
            "DELETE FROM nexjob_recurring_locks WHERE recurring_job_id = @id",
            new { id = recurringJobId });
    }

    /// <inheritdoc/>
    public async Task ReportProgressAsync(
        JobId jobId, int percent, string? message, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE nexjob_jobs SET progress_percent = @p, progress_message = @m WHERE id = @id",
            new { id = jobId.Value, p = percent, m = message });
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<JobRecord>> GetJobsByTagAsync(
        string tag, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        var rows = await conn.QueryAsync<JobRow>(
            "SELECT * FROM nexjob_jobs WHERE @tag = ANY(tags)",
            new { tag });
        return rows.Select(r => r.ToRecord()).ToList();
    }

    // ── Schema ────────────────────────────────────────────────────────────────

    private NpgsqlConnection Open() => new(_connectionString);
}
