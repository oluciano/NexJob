#pragma warning disable MA0004
using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using NexJob.Storage;

namespace NexJob.SqlServer;

/// <summary>
/// SQL Server-backed implementation of <see cref="IStorageProvider"/>.
/// Uses <c>WITH (UPDLOCK, READPAST)</c> for atomic job claiming, preventing
/// double-processing across multiple workers or server instances.
/// </summary>
public sealed class SqlServerStorageProvider : IStorageProvider
{
    private readonly string _connectionString;
    private readonly SqlConnection? _connection;

    /// <summary>
    /// Initialises the provider and applies all pending schema migrations.
    /// Acquires an application-level lock via <c>sp_getapplock</c> so only one instance migrates at a time.
    /// </summary>
    public SqlServerStorageProvider(string connectionString)
    {
        _connectionString = connectionString;
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
#pragma warning disable RS0030
        // Sync-over-async is acceptable here: runs once at startup, before any requests are served.
        new SchemaMigrator().MigrateAsync(_connectionString).GetAwaiter().GetResult();
#pragma warning restore RS0030
    }

    /// <summary>
    /// Initialises the provider with an existing <see cref="SqlConnection"/>.
    /// Migrations are NOT applied when using this constructor.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="options">The nex job options.</param>
    public SqlServerStorageProvider(SqlConnection connection, NexJobOptions options)
    {
        _connection = connection;
        _connectionString = connection.ConnectionString;
    }

    // ── EnqueueAsync ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<EnqueueResult> EnqueueAsync(JobRecord job, DuplicatePolicy duplicatePolicy = DuplicatePolicy.AllowAfterFailed, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        if (job.IdempotencyKey is not null)
        {
            await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var existing = await conn.QueryFirstOrDefaultAsync<(Guid Id, string Status)>(
                """
                SELECT TOP 1 id, status FROM nexjob_jobs WITH (UPDLOCK, ROWLOCK)
                WHERE idempotency_key = @key
                ORDER BY created_at DESC
                """,
                new { key = job.IdempotencyKey },
                tx);

            if (existing.Id != Guid.Empty)
            {
                var existingId = new JobId(existing.Id);
                var existingStatus = ParseStatus(existing.Status);

                var existingResult = ResolveDuplicate(existingId, existingStatus, duplicatePolicy);
                if (existingResult.WasRejected || IsActiveState(existingStatus))
                {
                    await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    return existingResult;
                }
            }

            try
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO nexjob_jobs
                        (id, job_type, input_type, input_json, schema_version, queue, priority, status,
                         idempotency_key, attempts, max_attempts, created_at, scheduled_at, parent_job_id, recurring_job_id, tags)
                    VALUES
                        (@Id, @JobType, @InputType, @InputJson, @SchemaVersion, @Queue, @Priority,
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
                        Tags = System.Text.Json.JsonSerializer.Serialize(job.Tags),
                    },
                    tx);

                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new EnqueueResult(job.Id, WasRejected: false);
            }
            catch (SqlException ex) when (IsUniqueConstraintViolation(ex))
            {
                // Race condition: unique constraint violation on idempotency_key.
                // Another thread inserted a job with the same idempotency key after our check.
                // Fetch the winning job and apply duplicate policy.
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);

                var winner = await conn.QueryFirstOrDefaultAsync<(Guid Id, string Status)>(
                    """
                    SELECT TOP 1 id, status FROM nexjob_jobs
                    WHERE idempotency_key = @key
                    ORDER BY created_at ASC
                    """,
                    new { key = job.IdempotencyKey });

                if (winner.Id == Guid.Empty)
                {
                    throw; // Should not happen; rethrow
                }

                var winnerId = new JobId(winner.Id);
                var winnerStatus = ParseStatus(winner.Status);

                return ResolveDuplicate(winnerId, winnerStatus, duplicatePolicy);
            }
        }

        await conn.ExecuteAsync(
            """
            INSERT INTO nexjob_jobs
                (id, job_type, input_type, input_json, schema_version, queue, priority, status,
                 idempotency_key, attempts, max_attempts, created_at, scheduled_at, parent_job_id, recurring_job_id, tags)
            VALUES
                (@Id, @JobType, @InputType, @InputJson, @SchemaVersion, @Queue, @Priority,
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
                Tags = System.Text.Json.JsonSerializer.Serialize(job.Tags),
            });

        return new EnqueueResult(job.Id, WasRejected: false);
    }

    // ── FetchNextAsync ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<JobRecord?> FetchNextAsync(
        IReadOnlyList<string> queues, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        // Promote due scheduled/retry jobs first
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_jobs
            SET status = 'Enqueued'
            WHERE status = 'Scheduled'
              AND (
                    (retry_at IS NOT NULL AND retry_at <= SYSUTCDATETIME())
                 OR (retry_at IS NULL AND scheduled_at IS NOT NULL AND scheduled_at <= SYSUTCDATETIME())
              )
            """, transaction: tx);

        // Build queue priority list for ordering
        var queueList = string.Join(",", queues.Select((q, i) => $"('{q.Replace("'", "''")}',{i})"));

        var row = await conn.QuerySingleOrDefaultAsync<JobRow>(
            $"""
            UPDATE nexjob_jobs
            SET status                = 'Processing',
                processing_started_at = SYSUTCDATETIME(),
                heartbeat_at          = SYSUTCDATETIME(),
                attempts              = attempts + 1
            OUTPUT INSERTED.*
            WHERE id = (
                SELECT TOP 1 j.id
                FROM nexjob_jobs j WITH (UPDLOCK, READPAST)
                INNER JOIN (VALUES {queueList}) AS q(name, ord) ON j.queue = q.name
                WHERE j.status = 'Enqueued'
                ORDER BY q.ord ASC, j.priority ASC, j.created_at ASC
            )
            """,
            transaction: tx);

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return row?.ToRecord();
    }

    // ── AcknowledgeAsync ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task AcknowledgeAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_jobs
            SET status = 'Succeeded', completed_at = SYSUTCDATETIME(), heartbeat_at = NULL
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
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

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
                SET status = 'Failed', completed_at = SYSUTCDATETIME(),
                    exception_message = @msg, exception_stack_trace = @stack,
                    heartbeat_at = NULL
                WHERE id = @id
                """,
                new { id = jobId.Value, msg = exception.Message, stack = exception.StackTrace });
        }
    }

    /// <inheritdoc/>
    public async Task SetExpiredAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        await conn.ExecuteAsync(
            """
            UPDATE nexjob_jobs
            SET status = 'Expired', completed_at = SYSUTCDATETIME(), heartbeat_at = NULL
            WHERE id = @id
            """,
            new { id = jobId.Value });
    }

    // ── UpdateHeartbeatAsync ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task UpdateHeartbeatAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "UPDATE nexjob_jobs SET heartbeat_at = SYSUTCDATETIME() WHERE id = @id",
            new { id = jobId.Value });
    }

    // ── Recurring jobs ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task UpsertRecurringJobAsync(
        RecurringJobRecord recurringJob, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(
            """
            MERGE nexjob_recurring_jobs WITH (HOLDLOCK) AS target
            USING (SELECT @RecurringJobId AS recurring_job_id) AS source
            ON target.recurring_job_id = source.recurring_job_id
            WHEN MATCHED THEN
                UPDATE SET
                    job_type           = @JobType,
                    input_type         = @InputType,
                    input_json         = @InputJson,
                    cron               = @Cron,
                    time_zone_id       = @TimeZoneId,
                    queue              = @Queue,
                    next_execution     = @NextExecution,
                    concurrency_policy = @ConcurrencyPolicy,
                    updated_at         = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (recurring_job_id, job_type, input_type, input_json, cron,
                        time_zone_id, queue, next_execution, concurrency_policy, created_at, updated_at,
                        cron_override, enabled, deleted_by_user)
                VALUES (@RecurringJobId, @JobType, @InputType, @InputJson, @Cron,
                        @TimeZoneId, @Queue, @NextExecution, @ConcurrencyPolicy, SYSUTCDATETIME(), SYSUTCDATETIME(),
                        NULL, 1, 0);
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
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
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
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_recurring_jobs
            SET next_execution = @next, last_execution = SYSUTCDATETIME(), updated_at = SYSUTCDATETIME()
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
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_recurring_jobs
            SET last_execution_status = @status, last_execution_error = @error, updated_at = SYSUTCDATETIME()
            WHERE recurring_job_id = @id
            """,
            new { id = recurringJobId, status = status.ToString(), error = errorMessage });
    }

    /// <inheritdoc/>
    public async Task DeleteRecurringJobAsync(
        string recurringJobId, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM nexjob_recurring_jobs WHERE recurring_job_id = @id",
            new { id = recurringJobId });
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RecurringJobRecord>> GetRecurringJobsAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await conn.QueryAsync<RecurringJobRow>(
            "SELECT * FROM nexjob_recurring_jobs ORDER BY recurring_job_id");
        return rows.Select(r => r.ToRecord()).ToList();
    }

    /// <inheritdoc/>
    public async Task<RecurringJobRecord?> GetRecurringJobByIdAsync(string recurringJobId, CancellationToken cancellationToken = default)
    {
        return await GetRecurringJobAsync(recurringJobId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<RecurringJobRecord?> GetRecurringJobAsync(string recurringJobId, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
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
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_recurring_jobs
            SET cron_override = @CronOverride, enabled = @Enabled, updated_at = SYSUTCDATETIME()
            WHERE recurring_job_id = @Id
            """,
            new { Id = recurringJobId, CronOverride = cronOverride, Enabled = enabled });
    }

    /// <inheritdoc/>
    public async Task ForceDeleteRecurringJobAsync(
        string recurringJobId, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM nexjob_jobs WHERE recurring_job_id = @id",
            new { id = recurringJobId });
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_recurring_jobs
            SET deleted_by_user = 1, enabled = 0, updated_at = SYSUTCDATETIME()
            WHERE recurring_job_id = @id
            """,
            new { id = recurringJobId });
    }

    /// <inheritdoc/>
    public async Task RestoreRecurringJobAsync(
        string recurringJobId, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_recurring_jobs
            SET deleted_by_user = 0, enabled = 1, updated_at = SYSUTCDATETIME()
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
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
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
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_jobs
            SET status = 'Enqueued'
            WHERE status = 'AwaitingContinuation' AND parent_job_id = @parentId
            """,
            new { parentId = parentJobId.Value });
    }

    // ── Server / Worker node tracking ─────────────────────────────────────────

    /// <inheritdoc/>
    public async Task RegisterServerAsync(ServerRecord server, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(
            """
            MERGE nexjob_servers WITH (HOLDLOCK) AS target
            USING (SELECT @Id AS id) AS source
            ON target.id = source.id
            WHEN MATCHED THEN
                UPDATE SET
                    worker_count = @WorkerCount,
                    queues       = @Queues,
                    heartbeat_at = @HeartbeatAt
            WHEN NOT MATCHED THEN
                INSERT (id, worker_count, queues, started_at, heartbeat_at)
                VALUES (@Id, @WorkerCount, @Queues, @StartedAt, @HeartbeatAt);
            """,
            new
            {
                server.Id,
                server.WorkerCount,
                Queues = System.Text.Json.JsonSerializer.Serialize(server.Queues),
                server.StartedAt,
                server.HeartbeatAt,
            });
    }

    /// <inheritdoc/>
    public async Task HeartbeatServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "UPDATE nexjob_servers SET heartbeat_at = SYSUTCDATETIME() WHERE id = @Id",
            new { Id = serverId });
    }

    /// <inheritdoc/>
    public async Task DeregisterServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM nexjob_servers WHERE id = @Id",
            new { Id = serverId });
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ServerRecord>> GetActiveServersAsync(TimeSpan activeTimeout, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - activeTimeout;
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var rows = await conn.QueryAsync(
            "SELECT id, worker_count, queues, started_at, heartbeat_at FROM nexjob_servers WHERE heartbeat_at >= @Cutoff ORDER BY id",
            new { Cutoff = cutoff });

        return rows.Select(r => new ServerRecord
        {
            Id = r.id,
            WorkerCount = r.worker_count,
            Queues = System.Text.Json.JsonSerializer.Deserialize<string[]>(r.queues ?? "[]") ?? Array.Empty<string>(),
            StartedAt = r.started_at,
            HeartbeatAt = r.heartbeat_at,
        }).ToList();
    }

    // ── Dashboard support ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<JobMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var counts = (await conn.QueryAsync<(string Status, int Count)>(
            "SELECT status, COUNT(*) AS count FROM nexjob_jobs GROUP BY status"))
            .ToDictionary(x => x.Status, x => x.Count, StringComparer.Ordinal);

        var throughput = (await conn.QueryAsync<(DateTimeOffset Hour, int Count)>(
            """
            SELECT DATEADD(HOUR, DATEDIFF(HOUR, 0, CAST(completed_at AS DATETIME2)), 0) AS hour,
                   COUNT(*) AS count
            FROM nexjob_jobs
            WHERE completed_at >= DATEADD(HOUR, -24, SYSUTCDATETIME())
              AND status IN ('Succeeded','Failed')
            GROUP BY DATEADD(HOUR, DATEDIFF(HOUR, 0, CAST(completed_at AS DATETIME2)), 0)
            ORDER BY hour
            """))
            .Select(r => new HourlyThroughput { Hour = r.Hour, Count = r.Count })
            .ToList();

        var recentFailures = (await conn.QueryAsync<JobRow>(
            "SELECT TOP 10 * FROM nexjob_jobs WHERE status = 'Failed' ORDER BY completed_at DESC"))
            .Select(r => r.ToRecord())
            .ToList();

        var recurringCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM nexjob_recurring_jobs");

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
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var where = new List<string>();
        var p = new DynamicParameters();

        if (filter.Status.HasValue)
        {
            where.Add("status = @status");
            p.Add("status", filter.Status.Value.ToString());
        }

        if (!string.IsNullOrWhiteSpace(filter.Queue))
        {
            where.Add("queue = @queue");
            p.Add("queue", filter.Queue);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            where.Add("(job_type LIKE @search OR CAST(id AS NVARCHAR(50)) LIKE @search)");
            p.Add("search", $"%{filter.Search.Trim()}%");
        }

        if (!string.IsNullOrEmpty(filter.RecurringJobId))
        {
            where.Add("recurring_job_id = @recurringJobId");
            p.Add("recurringJobId", filter.RecurringJobId);
        }

        var clause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : string.Empty;
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM nexjob_jobs {clause}", p).ConfigureAwait(false);

        var offset = (page - 1) * pageSize;
        p.Add("offset", offset);
        p.Add("pageSize", pageSize);

        var items = (await conn.QueryAsync<JobRow>(
            $"SELECT * FROM nexjob_jobs {clause} ORDER BY created_at DESC OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY", p))
            .Select(r => r.ToRecord())
            .ToList();

        return new PagedResult<JobRecord> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
    }

    /// <inheritdoc/>
    public async Task<JobRecord?> GetJobByIdAsync(JobId id, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await conn.QuerySingleOrDefaultAsync<JobRow>(
            "SELECT * FROM nexjob_jobs WHERE id = @id", new { id = id.Value });
        return row?.ToRecord();
    }

    /// <inheritdoc/>
    public async Task DeleteJobAsync(JobId id, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync("DELETE FROM nexjob_jobs WHERE id = @id", new { id = id.Value }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RequeueJobAsync(JobId id, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
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
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await conn.QueryAsync<(string Queue, int Enqueued, int Processing)>(
            """
            SELECT queue,
                   SUM(CASE WHEN status = 'Enqueued' THEN 1 ELSE 0 END) AS enqueued,
                   SUM(CASE WHEN status = 'Processing' THEN 1 ELSE 0 END) AS processing
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
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "UPDATE nexjob_jobs SET execution_logs = @Logs WHERE id = @Id",
            new { Id = jobId.Value, Logs = JsonSerializer.Serialize(logs) });
    }

    /// <inheritdoc/>
    public async Task CommitJobResultAsync(
        JobId jobId, JobExecutionResult result, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        var currentStatus = await conn.ExecuteScalarAsync<string?>(
            "SELECT status FROM nexjob_jobs WITH (UPDLOCK, ROWLOCK) WHERE id = @id",
            new { id = jobId.Value }, transaction: tx);

        if (IsTerminalStatus(currentStatus))
        {
            await tx.RollbackAsync(cancellationToken);
            return;
        }

        var logsJson = JsonSerializer.Serialize(result.Logs);

        if (result.Succeeded)
        {
            await ApplySuccessAsync(conn, tx, jobId, result, logsJson);
            await tx.CommitAsync(cancellationToken);
            return;
        }

        if (result.RetryAt.HasValue)
        {
            await ApplyRetryAsync(conn, tx, jobId, result, logsJson);
            await tx.CommitAsync(cancellationToken);
            return;
        }

        await ApplyFailureAsync(conn, tx, jobId, result, logsJson);
        await tx.CommitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> TryAcquireRecurringJobLockAsync(
        string recurringJobId, TimeSpan ttl, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM nexjob_recurring_locks WHERE expires_at < SYSUTCDATETIME()");
        try
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO nexjob_recurring_locks (recurring_job_id, expires_at)
                VALUES (@id, DATEADD(MILLISECOND, @ms, SYSUTCDATETIME()))
                """,
                new { id = recurringJobId, ms = (long)ttl.TotalMilliseconds });
            return true;
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
        {
            // Primary key / unique constraint violation — lock already held
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task ReleaseRecurringJobLockAsync(
        string recurringJobId, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "DELETE FROM nexjob_recurring_locks WHERE recurring_job_id = @id",
            new { id = recurringJobId });
    }

    /// <inheritdoc/>
    public async Task ReportProgressAsync(
        JobId jobId, int percent, string? message, CancellationToken ct = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(
            "UPDATE nexjob_jobs SET progress_percent = @p, progress_message = @m WHERE id = @id",
            new { id = jobId.Value, p = percent, m = message });
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<JobRecord>> GetJobsByTagAsync(
        string tag, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        // tags is stored as JSON array string, e.g. '["tenant:acme","region:us"]'
        var rows = await conn.QueryAsync<JobRow>(
            "SELECT * FROM nexjob_jobs WHERE tags LIKE @pattern",
            new { pattern = $"%\"{tag}\"%" });
        return rows.Select(r => r.ToRecord()).ToList();
    }

    /// <inheritdoc/>
    public async Task<int> PurgeJobsAsync(RetentionPolicy policy, CancellationToken cancellationToken = default)
    {
        await using var conn = Open();
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var deleted = 0;

        if (policy.RetainSucceeded > TimeSpan.Zero)
        {
            deleted += await conn.ExecuteAsync(
                """
                DELETE FROM nexjob_jobs
                WHERE status = 'Succeeded'
                  AND completed_at < DATEADD(SECOND, @seconds, SYSDATETIMEOFFSET())
                """,
                new { seconds = -(long)policy.RetainSucceeded.TotalSeconds, }).ConfigureAwait(false);
        }

        if (policy.RetainFailed > TimeSpan.Zero)
        {
            deleted += await conn.ExecuteAsync(
                """
                DELETE FROM nexjob_jobs
                WHERE status = 'Failed'
                  AND completed_at < DATEADD(SECOND, @seconds, SYSDATETIMEOFFSET())
                """,
                new { seconds = -(long)policy.RetainFailed.TotalSeconds, }).ConfigureAwait(false);
        }

        if (policy.RetainExpired > TimeSpan.Zero)
        {
            deleted += await conn.ExecuteAsync(
                """
                DELETE FROM nexjob_jobs
                WHERE status = 'Expired'
                  AND created_at < DATEADD(SECOND, @seconds, SYSDATETIMEOFFSET())
                """,
                new { seconds = -(long)policy.RetainExpired.TotalSeconds, }).ConfigureAwait(false);
        }

        return deleted;
    }

    // ── Schema ────────────────────────────────────────────────────────────────

    private static bool IsTerminalStatus(string? status) =>
        status is "Succeeded" or "Failed" or "Expired" or null;

    private static EnqueueResult ResolveDuplicate(JobId id, JobStatus status, DuplicatePolicy policy)
    {
        if (IsActiveState(status))
        {
            return new EnqueueResult(id, WasRejected: false);
        }

        var wasRejected = status == JobStatus.Failed
            ? policy is DuplicatePolicy.RejectIfFailed or DuplicatePolicy.RejectAlways
            : policy == DuplicatePolicy.RejectAlways;

        return new EnqueueResult(id, WasRejected: wasRejected);
    }

    private static JobStatus ParseStatus(string status) =>
        Enum.TryParse<JobStatus>(status, out var parsed) ? parsed : JobStatus.Failed;

    private static bool IsActiveState(JobStatus status) =>
        status is JobStatus.Enqueued or JobStatus.Processing or JobStatus.Scheduled or JobStatus.AwaitingContinuation;

    private static bool IsUniqueConstraintViolation(SqlException ex)
    {
        // SQL Server error 2627: violation of PRIMARY KEY or UNIQUE constraint
        // Error 2601: cannot insert duplicate key row (index-specific, but same root cause)
        return ex.Number is 2627 or 2601;
    }

    private SqlConnection Open() => _connection is not null ? new SqlConnection(_connection.ConnectionString) : new SqlConnection(_connectionString);

    private async Task ApplySuccessAsync(
        IDbConnection conn, IDbTransaction tx, JobId jobId, JobExecutionResult result,
        string logsJson)
    {
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_jobs
            SET status = 'Succeeded', completed_at = SYSUTCDATETIME(), heartbeat_at = NULL, execution_logs = @Logs
            WHERE id = @id
            """,
            new { id = jobId.Value, Logs = logsJson },
            transaction: tx);

        await conn.ExecuteAsync(
            """
            UPDATE nexjob_jobs
            SET status = 'Enqueued', scheduled_at = NULL
            WHERE parent_job_id = @id AND status = 'AwaitingContinuation'
            """,
            new { id = jobId.Value },
            transaction: tx);

        if (result.RecurringJobId is not null)
        {
            await conn.ExecuteAsync(
                """
                UPDATE nexjob_recurring_jobs
                SET last_execution_status = 'Succeeded', last_execution_error = NULL, updated_at = SYSUTCDATETIME()
                WHERE recurring_job_id = @rid
                """,
                new { rid = result.RecurringJobId },
                transaction: tx);
        }
    }

    private async Task ApplyRetryAsync(
        IDbConnection conn, IDbTransaction tx, JobId jobId, JobExecutionResult result,
        string logsJson)
    {
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_jobs
            SET status = 'Scheduled', retry_at = @retryAt,
                exception_message = @msg, exception_stack_trace = @stack,
                heartbeat_at = NULL, execution_logs = @Logs
            WHERE id = @id
            """,
            new
            {
                id = jobId.Value,
                retryAt = result.RetryAt!.Value,
                msg = result.Exception?.Message,
                stack = result.Exception?.StackTrace,
                Logs = logsJson,
            },
            transaction: tx);
    }

    private async Task ApplyFailureAsync(
        IDbConnection conn, IDbTransaction tx, JobId jobId, JobExecutionResult result,
        string logsJson)
    {
        await conn.ExecuteAsync(
            """
            UPDATE nexjob_jobs
            SET status = 'Failed', completed_at = SYSUTCDATETIME(), retry_at = NULL,
                exception_message = @msg, exception_stack_trace = @stack,
                heartbeat_at = NULL, execution_logs = @Logs
            WHERE id = @id
            """,
            new
            {
                id = jobId.Value,
                msg = result.Exception?.Message,
                stack = result.Exception?.StackTrace,
                Logs = logsJson,
            },
            transaction: tx);

        if (result.RecurringJobId is not null)
        {
            await conn.ExecuteAsync(
                """
                UPDATE nexjob_recurring_jobs
                SET last_execution_status = 'Failed', last_execution_error = @msg, updated_at = SYSUTCDATETIME()
                WHERE recurring_job_id = @rid
                """,
                new { rid = result.RecurringJobId, msg = result.Exception?.Message },
                transaction: tx);
        }
    }
}
#pragma warning restore MA0004
