using System.Globalization;
using System.Text.Json;
using NexJob.Storage;
using StackExchange.Redis;

namespace NexJob.Redis;

/// <summary>
/// Redis-backed implementation of <see cref="IStorageProvider"/>.
/// All mutations that require atomicity use Lua scripts evaluated server-side,
/// guaranteeing consistent state even with multiple worker instances.
/// </summary>
public sealed class RedisStorageProvider : IStorageProvider
{
    private const string ProcessingKey = "nexjob:processing";
    private const string ScheduledKey = "nexjob:scheduled";
    private const string RecurringAllKey = "nexjob:recurring:all";
    private const string ThroughputKey = "nexjob:throughput";
    private const string ServersAllKey = "nexjob:servers:all";

    private static readonly JsonSerializerOptions JsonOpts = new();

    private static readonly LuaScript FetchNextScript = LuaScript.Prepare(
        """
        for i = 1, #KEYS do
          local zkey = KEYS[i]
          local members = redis.call('ZRANGE', zkey, 0, 0)
          if #members > 0 then
            local id = members[1]
            redis.call('ZREM', zkey, id)
            local jobKey = 'nexjob:jobs:' .. id
            local now = ARGV[1]
            redis.call('HSET', jobKey,
              'status', 'Processing',
              'processingStartedAt', now,
              'heartbeatAt', now)
            redis.call('HINCRBY', jobKey, 'attempts', 1)
            redis.call('HSET', 'nexjob:processing', id, now)
            return redis.call('HGETALL', jobKey)
          end
        end
        return false
        """);

    private static readonly LuaScript CommitJobResultScript = LuaScript.Prepare(
        """
        local jobKey = 'nexjob:jobs:' .. ARGV[1]
        local status = redis.call('HGET', jobKey, 'status')

        -- Idempotency guard: if already terminal, return without error
        if status == 'Succeeded' or status == 'Failed' or status == 'Expired' or not status then
          return 0
        end

        if ARGV[2] == 'true' then
          -- Success path
          redis.call('HSET', jobKey, 'status', 'Succeeded', 'completedAt', ARGV[3], 'heartbeatAt', '')
          redis.call('HDEL', 'nexjob:processing', ARGV[1])

          -- Enqueue continuations
          local contKey = 'nexjob:continuations:' .. ARGV[1]
          local continuations = redis.call('SMEMBERS', contKey)
          for i = 1, #continuations do
            local childId = continuations[i]
            redis.call('HSET', 'nexjob:jobs:' .. childId, 'status', 'Enqueued', 'scheduledAt', '')
            -- Requeue the child (simple approach: add back to queue with priority)
          end

          -- Update recurring job if applicable
          if ARGV[4] ~= '' then
            local rKey = 'nexjob:recurring:' .. ARGV[4]
            redis.call('HSET', rKey, 'lastExecutionStatus', 'Succeeded', 'lastExecutionError', '', 'updatedAt', ARGV[3])
          end
        else
          -- Failure path
          if ARGV[5] ~= '' then
            -- Has retry
            redis.call('HSET', jobKey, 'status', 'Scheduled', 'retryAt', ARGV[5],
                       'exceptionMessage', ARGV[6], 'exceptionStackTrace', ARGV[7], 'heartbeatAt', '')
            redis.call('ZADD', 'nexjob:scheduled', tonumber(ARGV[8]), ARGV[1])
            redis.call('HDEL', 'nexjob:processing', ARGV[1])
          else
            -- No retry — dead letter
            redis.call('HSET', jobKey, 'status', 'Failed', 'completedAt', ARGV[3],
                       'exceptionMessage', ARGV[6], 'exceptionStackTrace', ARGV[7], 'heartbeatAt', '')
            redis.call('HDEL', 'nexjob:processing', ARGV[1])

            -- Update recurring job if applicable and no retry
            if ARGV[4] ~= '' then
              local rKey = 'nexjob:recurring:' .. ARGV[4]
              redis.call('HSET', rKey, 'lastExecutionStatus', 'Failed', 'lastExecutionError', ARGV[6], 'updatedAt', ARGV[3])
            end
          end
        end

        return 1
        """);

    private readonly IDatabase _db;

    /// <summary>
    /// Initialises the provider with an existing <see cref="IDatabase"/> instance.
    /// </summary>
    public RedisStorageProvider(IDatabase database)
    {
        _db = database;
    }

    /// <inheritdoc/>
    public async Task<EnqueueResult> EnqueueAsync(JobRecord job, DuplicatePolicy duplicatePolicy = DuplicatePolicy.AllowAfterFailed, CancellationToken cancellationToken = default)
    {
        if (job.IdempotencyKey is not null)
        {
            var idemKey = IdempotencyRedisKey(job.IdempotencyKey);
            var existingIdStr = await _db.StringGetAsync(idemKey).ConfigureAwait(false);

            if (existingIdStr.HasValue)
            {
                var existingId = new JobId(Guid.Parse(existingIdStr.ToString()!));
                var jobHash = await _db.HashGetAllAsync(JobKey(existingIdStr.ToString()!)).ConfigureAwait(false);
                var existingStatus = GetStatusFromHash(jobHash);

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

                await _db.KeyDeleteAsync(idemKey).ConfigureAwait(false);
            }
        }

        var id = job.Id.Value.ToString();
        await _db.HashSetAsync(JobKey(id), BuildJobHash(job)).ConfigureAwait(false);

        if (job.IdempotencyKey is not null)
        {
            await _db.StringSetAsync(IdempotencyRedisKey(job.IdempotencyKey), id, TimeSpan.FromDays(7)).ConfigureAwait(false);
        }

        if (job.Status == JobStatus.AwaitingContinuation && job.ParentJobId.HasValue)
        {
            await _db.SetAddAsync(ContinuationSetKey(job.ParentJobId.Value.Value), id).ConfigureAwait(false);
        }
        else if (job.Status == JobStatus.Scheduled && job.ScheduledAt.HasValue)
        {
            await _db.SortedSetAddAsync(ScheduledKey, id, job.ScheduledAt.Value.ToUnixTimeMilliseconds()).ConfigureAwait(false);
        }
        else if (job.Status == JobStatus.Enqueued)
        {
            await _db.SortedSetAddAsync(QueueKey(job.Queue), id, QueueScore((int)job.Priority, job.CreatedAt)).ConfigureAwait(false);
        }

        return new EnqueueResult(job.Id, WasRejected: false);
    }

    /// <inheritdoc/>
    public async Task<JobRecord?> FetchNextAsync(
        IReadOnlyList<string> queues, CancellationToken cancellationToken = default)
    {
        await PromoteScheduledJobsAsync().ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var keys = queues.Select(q => (RedisKey)QueueKey(q)).ToArray();
        var args = new RedisValue[] { now };

        var rawResult = await _db.ScriptEvaluateAsync(FetchNextScript.ExecutableScript, keys, args).ConfigureAwait(false);
        if (rawResult.IsNull)
        {
            return null;
        }

        var resultItems = (RedisValue[])rawResult!;
        if (resultItems.Length == 0)
        {
            return null;
        }

        return HashToRecord(ParseFlatArray(resultItems));
    }

    /// <inheritdoc/>
    public async Task AcknowledgeAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        var id = jobId.Value.ToString();
        var now = DateTimeOffset.UtcNow;

        await _db.HashSetAsync(JobKey(id), new[]
        {
            new HashEntry("status", "Succeeded"),
            new HashEntry("completedAt", now.ToString("O", CultureInfo.InvariantCulture)),
            new HashEntry("heartbeatAt", string.Empty),
        }).ConfigureAwait(false);

        await _db.HashDeleteAsync(ProcessingKey, id).ConfigureAwait(false);
        await _db.SortedSetAddAsync(ThroughputKey, id, now.ToUnixTimeMilliseconds()).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SetFailedAsync(
        JobId jobId, Exception exception, DateTimeOffset? retryAt,
        CancellationToken cancellationToken = default)
    {
        var id = jobId.Value.ToString();
        await _db.HashDeleteAsync(ProcessingKey, id).ConfigureAwait(false);

        if (retryAt.HasValue)
        {
            await _db.HashSetAsync(JobKey(id), new[]
            {
                new HashEntry("status", "Scheduled"),
                new HashEntry("retryAt", retryAt.Value.ToString("O", CultureInfo.InvariantCulture)),
                new HashEntry("exceptionMessage", exception.Message),
                new HashEntry("exceptionStackTrace", exception.StackTrace ?? string.Empty),
                new HashEntry("heartbeatAt", string.Empty),
            }).ConfigureAwait(false);
            await _db.SortedSetAddAsync(ScheduledKey, id, retryAt.Value.ToUnixTimeMilliseconds()).ConfigureAwait(false);
        }
        else
        {
            var now = DateTimeOffset.UtcNow;
            await _db.HashSetAsync(JobKey(id), new[]
            {
                new HashEntry("status", "Failed"),
                new HashEntry("completedAt", now.ToString("O", CultureInfo.InvariantCulture)),
                new HashEntry("exceptionMessage", exception.Message),
                new HashEntry("exceptionStackTrace", exception.StackTrace ?? string.Empty),
                new HashEntry("heartbeatAt", string.Empty),
            }).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task SetExpiredAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        var id = jobId.Value.ToString();
        var now = DateTimeOffset.UtcNow;

        await _db.HashDeleteAsync(ProcessingKey, id).ConfigureAwait(false);
        await _db.HashSetAsync(JobKey(id), new[]
        {
            new HashEntry("status", "Expired"),
            new HashEntry("completedAt", now.ToString("O", CultureInfo.InvariantCulture)),
            new HashEntry("heartbeatAt", string.Empty),
        }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UpdateHeartbeatAsync(JobId jobId, CancellationToken cancellationToken = default)
    {
        var id = jobId.Value.ToString();
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await _db.HashSetAsync(JobKey(id), "heartbeatAt", now).ConfigureAwait(false);
        await _db.HashSetAsync(ProcessingKey, id, now).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task UpsertRecurringJobAsync(
        RecurringJobRecord recurringJob, CancellationToken cancellationToken = default)
    {
        var id = recurringJob.RecurringJobId;
        var key = RecurringKey(id);
        var isNew = !await _db.KeyExistsAsync(key).ConfigureAwait(false);

        var fields = new List<HashEntry>
        {
            new("recurringJobId", id),
            new("jobType", recurringJob.JobType),
            new("inputType", recurringJob.InputType),
            new("inputJson", recurringJob.InputJson),
            new("cron", recurringJob.Cron),
            new("timeZoneId", recurringJob.TimeZoneId ?? "UTC"),
            new("queue", recurringJob.Queue),
            new("nextExecution", recurringJob.NextExecution?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty),
            new("concurrencyPolicy", recurringJob.ConcurrencyPolicy.ToString()),
            new("createdAt", recurringJob.CreatedAt.ToString("O", CultureInfo.InvariantCulture)),
            new("updatedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
        };

        if (isNew)
        {
            fields.Add(new HashEntry("cronOverride", string.Empty));
            fields.Add(new HashEntry("enabled", "true"));
            fields.Add(new HashEntry("deletedByUser", "false"));
        }

        await _db.HashSetAsync(key, fields.ToArray()).ConfigureAwait(false);
        await _db.SetAddAsync(RecurringAllKey, id).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RecurringJobRecord>> GetDueRecurringJobsAsync(
        DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        var allIds = await _db.SetMembersAsync(RecurringAllKey).ConfigureAwait(false);
        var result = new List<RecurringJobRecord>();

        foreach (var idVal in allIds)
        {
            var hash = await _db.HashGetAllAsync(RecurringKey(idVal.ToString())).ConfigureAwait(false);
            if (hash.Length == 0)
            {
                continue;
            }

            var dict = ParseHash(hash);
            var nextExecStr = dict.GetValueOrDefault("nextExecution", string.Empty);
            if (!DateTimeOffset.TryParse(nextExecStr, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var nextExec))
            {
                continue;
            }

            if (nextExec <= utcNow)
            {
                result.Add(HashToRecurring(dict));
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task SetRecurringJobNextExecutionAsync(
        string recurringJobId, DateTimeOffset nextExecution,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await _db.HashSetAsync(RecurringKey(recurringJobId), new[]
        {
            new HashEntry("nextExecution", nextExecution.ToString("O", CultureInfo.InvariantCulture)),
            new HashEntry("lastExecution", now),
            new HashEntry("updatedAt", now),
        }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SetRecurringJobLastExecutionResultAsync(
        string recurringJobId, JobStatus status, string? errorMessage,
        CancellationToken cancellationToken = default)
    {
        await _db.HashSetAsync(RecurringKey(recurringJobId), new[]
        {
            new HashEntry("lastExecutionStatus", status.ToString()),
            new HashEntry("lastExecutionError", errorMessage ?? string.Empty),
            new HashEntry("updatedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
        }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteRecurringJobAsync(
        string recurringJobId, CancellationToken cancellationToken = default)
    {
        await _db.KeyDeleteAsync(RecurringKey(recurringJobId)).ConfigureAwait(false);
        await _db.SetRemoveAsync(RecurringAllKey, recurringJobId).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RecurringJobRecord>> GetRecurringJobsAsync(
        CancellationToken cancellationToken = default)
    {
        var allIds = await _db.SetMembersAsync(RecurringAllKey).ConfigureAwait(false);
        var result = new List<RecurringJobRecord>();

        foreach (var idVal in allIds)
        {
            var hash = await _db.HashGetAllAsync(RecurringKey(idVal.ToString())).ConfigureAwait(false);
            if (hash.Length > 0)
            {
                result.Add(HashToRecurring(ParseHash(hash)));
            }
        }

        return result.OrderBy(r => r.RecurringJobId, StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc/>
    public async Task<RecurringJobRecord?> GetRecurringJobByIdAsync(
        string recurringJobId, CancellationToken cancellationToken = default)
    {
        return await GetRecurringJobAsync(recurringJobId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<RecurringJobRecord?> GetRecurringJobAsync(
        string recurringJobId, CancellationToken cancellationToken = default)
    {
        var hash = await _db.HashGetAllAsync(RecurringKey(recurringJobId)).ConfigureAwait(false);
        return hash.Length == 0 ? null : HashToRecurring(ParseHash(hash));
    }

    /// <inheritdoc/>
    public async Task UpdateRecurringJobConfigAsync(
        string recurringJobId, string? cronOverride, bool enabled,
        CancellationToken cancellationToken = default)
    {
        await _db.HashSetAsync(RecurringKey(recurringJobId), new[]
        {
            new HashEntry("cronOverride", cronOverride ?? string.Empty),
            new HashEntry("enabled", enabled ? "true" : "false"),
            new HashEntry("updatedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
        }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ForceDeleteRecurringJobAsync(
        string recurringJobId, CancellationToken cancellationToken = default)
    {
        await _db.HashSetAsync(RecurringKey(recurringJobId), new[]
        {
            new HashEntry("deletedByUser", "true"),
            new HashEntry("enabled", "false"),
            new HashEntry("updatedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
        }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RestoreRecurringJobAsync(
        string recurringJobId, CancellationToken cancellationToken = default)
    {
        await _db.HashSetAsync(RecurringKey(recurringJobId), new[]
        {
            new HashEntry("deletedByUser", "false"),
            new HashEntry("enabled", "true"),
            new HashEntry("updatedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
        }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RequeueOrphanedJobsAsync(
        TimeSpan heartbeatTimeout, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - heartbeatTimeout;
        var processingEntries = await _db.HashGetAllAsync(ProcessingKey).ConfigureAwait(false);

        foreach (var entry in processingEntries)
        {
            var id = entry.Name.ToString();
            if (!DateTimeOffset.TryParse(entry.Value.ToString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var heartbeat))
            {
                continue;
            }

            if (heartbeat >= cutoff)
            {
                continue;
            }

            var jobHash = await _db.HashGetAllAsync(JobKey(id)).ConfigureAwait(false);
            if (jobHash.Length == 0)
            {
                await _db.HashDeleteAsync(ProcessingKey, id).ConfigureAwait(false);
                continue;
            }

            var dict = ParseHash(jobHash);
            var queue = dict.GetValueOrDefault("queue", "default");
            var priorityStr = dict.GetValueOrDefault("priority", "3");
            var createdAtStr = dict.GetValueOrDefault("createdAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            var priority = int.TryParse(priorityStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 3;
            var createdAt = DateTimeOffset.TryParse(createdAtStr, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var ca) ? ca : DateTimeOffset.UtcNow;

            await _db.HashSetAsync(JobKey(id), new[]
            {
                new HashEntry("status", "Enqueued"),
                new HashEntry("heartbeatAt", string.Empty),
                new HashEntry("processingStartedAt", string.Empty),
            }).ConfigureAwait(false);
            await _db.HashDeleteAsync(ProcessingKey, id).ConfigureAwait(false);
            await _db.SortedSetAddAsync(QueueKey(queue), id, QueueScore(priority, createdAt)).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task EnqueueContinuationsAsync(
        JobId parentJobId, CancellationToken cancellationToken = default)
    {
        var continuationKey = ContinuationSetKey(parentJobId.Value);
        var childIds = await _db.SetMembersAsync(continuationKey).ConfigureAwait(false);

        foreach (var childIdVal in childIds)
        {
            var childId = childIdVal.ToString();
            var jobHash = await _db.HashGetAllAsync(JobKey(childId)).ConfigureAwait(false);
            if (jobHash.Length == 0)
            {
                continue;
            }

            var dict = ParseHash(jobHash);
            if (!string.Equals(dict.GetValueOrDefault("status"), "AwaitingContinuation", StringComparison.Ordinal))
            {
                continue;
            }

            var queue = dict.GetValueOrDefault("queue", "default");
            var priorityStr = dict.GetValueOrDefault("priority", "3");
            var createdAtStr = dict.GetValueOrDefault("createdAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            var priority = int.TryParse(priorityStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pri) ? pri : 3;
            var createdAt = DateTimeOffset.TryParse(createdAtStr, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var ca) ? ca : DateTimeOffset.UtcNow;

            await _db.HashSetAsync(JobKey(childId), "status", "Enqueued").ConfigureAwait(false);
            await _db.SortedSetAddAsync(QueueKey(queue), childId, QueueScore(priority, createdAt)).ConfigureAwait(false);
        }

        await _db.KeyDeleteAsync(continuationKey).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<JobMetrics> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var recentFailures = new List<JobRecord>();

        await foreach (var key in ScanJobKeysAsync().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var hash = await _db.HashGetAllAsync(key).ConfigureAwait(false);
            if (hash.Length == 0)
            {
                continue;
            }

            var dict = ParseHash(hash);
            var s = dict.GetValueOrDefault("status", string.Empty);
            counts.TryGetValue(s, out var cnt);
            counts[s] = cnt + 1;

            if (string.Equals(s, "Failed", StringComparison.Ordinal))
            {
                recentFailures.Add(HashToRecord(dict));
            }
        }

        var cutoffMs = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeMilliseconds();
        var throughputMembers = await _db.SortedSetRangeByScoreWithScoresAsync(
            ThroughputKey, cutoffMs, double.PositiveInfinity).ConfigureAwait(false);

        var hourBuckets = throughputMembers
            .GroupBy(m =>
            {
                var ts = DateTimeOffset.FromUnixTimeMilliseconds((long)m.Score).UtcDateTime;
                return new DateTimeOffset(ts.Year, ts.Month, ts.Day, ts.Hour, 0, 0, TimeSpan.Zero);
            })
            .Select(g => new HourlyThroughput { Hour = g.Key, Count = g.Count() })
            .OrderBy(h => h.Hour)
            .ToList();

        var recurringCount = (int)await _db.SetLengthAsync(RecurringAllKey).ConfigureAwait(false);

        return new JobMetrics
        {
            Enqueued = counts.GetValueOrDefault("Enqueued"),
            Processing = counts.GetValueOrDefault("Processing"),
            Succeeded = counts.GetValueOrDefault("Succeeded"),
            Failed = counts.GetValueOrDefault("Failed"),
            Scheduled = counts.GetValueOrDefault("Scheduled"),
            Recurring = recurringCount,
            HourlyThroughput = hourBuckets,
            RecentFailures = recentFailures
                .OrderByDescending(j => j.CompletedAt)
                .Take(10)
                .ToList(),
        };
    }

    /// <inheritdoc/>
    public async Task<PagedResult<JobRecord>> GetJobsAsync(
        JobFilter filter, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        var all = new List<JobRecord>();

        await foreach (var key in ScanJobKeysAsync().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var hash = await _db.HashGetAllAsync(key).ConfigureAwait(false);
            if (hash.Length == 0)
            {
                continue;
            }

            var record = HashToRecord(ParseHash(hash));
            if (MatchesFilter(record, filter))
            {
                all.Add(record);
            }
        }

        all = all.OrderByDescending(j => j.CreatedAt).ToList();
        var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new PagedResult<JobRecord>
        {
            Items = items,
            TotalCount = all.Count,
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <inheritdoc/>
    public async Task<JobRecord?> GetJobByIdAsync(
        JobId id, CancellationToken cancellationToken = default)
    {
        var hash = await _db.HashGetAllAsync(JobKey(id.Value.ToString())).ConfigureAwait(false);
        return hash.Length == 0 ? null : HashToRecord(ParseHash(hash));
    }

    /// <inheritdoc/>
    public async Task DeleteJobAsync(JobId id, CancellationToken cancellationToken = default)
    {
        var idStr = id.Value.ToString();
        await _db.KeyDeleteAsync(JobKey(idStr)).ConfigureAwait(false);
        await _db.KeyDeleteAsync(LogsKey(idStr)).ConfigureAwait(false);
        await _db.HashDeleteAsync(ProcessingKey, idStr).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task RequeueJobAsync(JobId id, CancellationToken cancellationToken = default)
    {
        var idStr = id.Value.ToString();
        var hash = await _db.HashGetAllAsync(JobKey(idStr)).ConfigureAwait(false);
        if (hash.Length == 0)
        {
            return;
        }

        var dict = ParseHash(hash);
        var queue = dict.GetValueOrDefault("queue", "default");
        var createdAtStr = dict.GetValueOrDefault("createdAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        var createdAt = DateTimeOffset.TryParse(createdAtStr, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var ca) ? ca : DateTimeOffset.UtcNow;

        await _db.HashSetAsync(JobKey(idStr), new[]
        {
            new HashEntry("status", "Enqueued"),
            new HashEntry("attempts", "0"),
            new HashEntry("retryAt", string.Empty),
            new HashEntry("completedAt", string.Empty),
            new HashEntry("exceptionMessage", string.Empty),
            new HashEntry("exceptionStackTrace", string.Empty),
        }).ConfigureAwait(false);
        await _db.SortedSetAddAsync(QueueKey(queue), idStr, QueueScore(3, createdAt)).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<QueueMetrics>> GetQueueMetricsAsync(
        CancellationToken cancellationToken = default)
    {
        var metrics = new Dictionary<string, (int Enqueued, int Processing)>(StringComparer.Ordinal);

        await foreach (var key in ScanJobKeysAsync().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var fields = await _db.HashGetAsync(key, new RedisValue[] { "status", "queue" }).ConfigureAwait(false);
            var status = fields[0].ToString();
            var queue = fields[1].ToString();
            if (string.IsNullOrEmpty(queue))
            {
                continue;
            }

            metrics.TryGetValue(queue, out var current);
            metrics[queue] = status switch
            {
                "Enqueued" => (current.Enqueued + 1, current.Processing),
                "Processing" => (current.Enqueued, current.Processing + 1),
                _ => current,
            };
        }

        return metrics
            .Select(kvp => new QueueMetrics
            {
                Queue = kvp.Key,
                Enqueued = kvp.Value.Enqueued,
                Processing = kvp.Value.Processing,
            })
            .OrderBy(q => q.Queue, StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task SaveExecutionLogsAsync(
        JobId jobId, IReadOnlyList<JobExecutionLog> logs,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(logs, JsonOpts);
        var idStr = jobId.Value.ToString();
        await _db.StringSetAsync(LogsKey(idStr), json).ConfigureAwait(false);
        await _db.HashSetAsync(JobKey(idStr), "executionLogs", json).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task CommitJobResultAsync(
        JobId jobId, JobExecutionResult result, CancellationToken cancellationToken = default)
    {
        var idStr = jobId.Value.ToString();
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var logsJson = JsonSerializer.Serialize(result.Logs, JsonOpts);

        var args = new RedisValue[]
        {
            idStr,
            result.Succeeded ? "true" : "false",
            now,
            result.RecurringJobId ?? string.Empty,
            result.RetryAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            result.Exception?.Message ?? string.Empty,
            result.Exception?.StackTrace ?? string.Empty,
            result.RetryAt?.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) ?? "0",
        };

        // Execute atomic state transitions via Lua script
        await _db.ScriptEvaluateAsync(CommitJobResultScript.ExecutableScript, keys: null, values: args).ConfigureAwait(false);

        // Persist logs (non-critical for atomicity)
        await _db.StringSetAsync(LogsKey(idStr), logsJson).ConfigureAwait(false);
        await _db.HashSetAsync(JobKey(idStr), "executionLogs", logsJson).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> TryAcquireRecurringJobLockAsync(
        string recurringJobId, TimeSpan ttl, CancellationToken ct = default)
    {
        var key = (RedisKey)$"nexjob:lock:recurring:{recurringJobId}";
        return await _db.StringSetAsync(key, "1", ttl, When.NotExists).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ReleaseRecurringJobLockAsync(
        string recurringJobId, CancellationToken ct = default)
    {
        var key = (RedisKey)$"nexjob:lock:recurring:{recurringJobId}";
        await _db.KeyDeleteAsync(key).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ReportProgressAsync(
        JobId jobId, int percent, string? message, CancellationToken ct = default)
    {
        var key = (RedisKey)JobKey(jobId.Value.ToString());
        await _db.HashSetAsync(key,
        [
            new HashEntry("progressPercent", percent.ToString(CultureInfo.InvariantCulture)),
            new HashEntry("progressMessage", message ?? string.Empty),
        ]).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<JobRecord>> GetJobsByTagAsync(
        string tag, CancellationToken cancellationToken = default)
    {
        var results = new List<JobRecord>();
        await foreach (var key in ScanJobKeysAsync().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var hash = await _db.HashGetAllAsync(key).ConfigureAwait(false);
            if (hash.Length == 0)
            {
                continue;
            }

            var d = ParseHash(hash);
            var tagsRaw = d.GetValueOrDefault("tags", string.Empty);
            var tags = DeserializeTags(tagsRaw);
            if (tags.Contains(tag, StringComparer.Ordinal))
            {
                results.Add(HashToRecord(d));
            }
        }

        return results;
    }

    // ── Server / Worker node tracking ─────────────────────────────────────────

    /// <inheritdoc/>
    public async Task RegisterServerAsync(ServerRecord server, CancellationToken cancellationToken = default)
    {
        var key = ServerKey(server.Id);
        var fields = new HashEntry[]
        {
            new("id", server.Id),
            new("workerCount", server.WorkerCount.ToString(CultureInfo.InvariantCulture)),
            new("queues", JsonSerializer.Serialize(server.Queues, JsonOpts)),
            new("startedAt", server.StartedAt.ToString("O", CultureInfo.InvariantCulture)),
            new("heartbeatAt", server.HeartbeatAt.ToString("O", CultureInfo.InvariantCulture)),
        };

        await _db.HashSetAsync(key, fields).ConfigureAwait(false);
        await _db.SetAddAsync(ServersAllKey, server.Id).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task HeartbeatServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        await _db.HashSetAsync(ServerKey(serverId), "heartbeatAt", now).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeregisterServerAsync(string serverId, CancellationToken cancellationToken = default)
    {
        await _db.KeyDeleteAsync(ServerKey(serverId)).ConfigureAwait(false);
        await _db.SetRemoveAsync(ServersAllKey, serverId).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ServerRecord>> GetActiveServersAsync(TimeSpan activeTimeout, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - activeTimeout;
        var allIds = await _db.SetMembersAsync(ServersAllKey).ConfigureAwait(false);
        var result = new List<ServerRecord>();

        foreach (var idVal in allIds)
        {
            var id = idVal.ToString();
            var hash = await _db.HashGetAllAsync(ServerKey(id)).ConfigureAwait(false);
            if (hash.Length == 0)
            {
                continue;
            }

            var dict = ParseHash(hash);
            var heartbeatStr = dict.GetValueOrDefault("heartbeatAt", string.Empty);
            if (!DateTimeOffset.TryParse(heartbeatStr, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var heartbeat))
            {
                continue;
            }

            if (heartbeat < cutoff)
            {
                continue;
            }

            var queuesRaw = dict.GetValueOrDefault("queues", string.Empty);
            var queues = string.IsNullOrEmpty(queuesRaw)
                ? Array.Empty<string>()
                : JsonSerializer.Deserialize<string[]>(queuesRaw, JsonOpts) ?? Array.Empty<string>();

            result.Add(new ServerRecord
            {
                Id = id,
                WorkerCount = int.TryParse(dict.GetValueOrDefault("workerCount", "1"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var wc) ? wc : 1,
                Queues = queues,
                StartedAt = DateTimeOffset.TryParse(dict.GetValueOrDefault("startedAt"), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var sa) ? sa : DateTimeOffset.UtcNow,
                HeartbeatAt = heartbeat,
            });
        }

        return result.OrderBy(s => s.Id, StringComparer.Ordinal).ToList();
    }

    /// <inheritdoc/>
    public async Task<int> PurgeJobsAsync(RetentionPolicy policy, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var deleted = 0;

        await foreach (var key in ScanJobKeysAsync().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var hash = await _db.HashGetAllAsync(key).ConfigureAwait(false);
            if (hash.Length == 0)
            {
                continue;
            }

            var dict = ParseHash(hash);
            var status = GetStatusFromHash(hash);

            DateTimeOffset? cutoff = null;
            TimeSpan retention = TimeSpan.Zero;

            switch (status)
            {
                case JobStatus.Succeeded when policy.RetainSucceeded > TimeSpan.Zero:
                    {
                        var completedAtStr = dict.GetValueOrDefault("completedAt");
                        if (completedAtStr != null
                            && DateTimeOffset.TryParse(completedAtStr, CultureInfo.InvariantCulture,
                                DateTimeStyles.RoundtripKind, out var dt))
                        {
                            cutoff = dt;
                        }

                        retention = policy.RetainSucceeded;
                        break;
                    }

                case JobStatus.Failed when policy.RetainFailed > TimeSpan.Zero:
                    {
                        var completedAtStr = dict.GetValueOrDefault("completedAt");
                        if (completedAtStr != null
                            && DateTimeOffset.TryParse(completedAtStr, CultureInfo.InvariantCulture,
                                DateTimeStyles.RoundtripKind, out var dt))
                        {
                            cutoff = dt;
                        }

                        retention = policy.RetainFailed;
                        break;
                    }

                case JobStatus.Expired when policy.RetainExpired > TimeSpan.Zero:
                    {
                        var createdAtStr = dict.GetValueOrDefault("createdAt");
                        if (createdAtStr != null
                            && DateTimeOffset.TryParse(createdAtStr, CultureInfo.InvariantCulture,
                                DateTimeStyles.RoundtripKind, out var dt))
                        {
                            cutoff = dt;
                        }

                        retention = policy.RetainExpired;
                        break;
                    }
            }

            if (cutoff.HasValue && now - cutoff.Value > retention)
            {
                await _db.KeyDeleteAsync(key).ConfigureAwait(false);
                deleted++;
            }
        }

        return deleted;
    }

    // ── Private static helpers ────────────────────────────────────────────────

    private static string JobKey(string id) => $"nexjob:jobs:{id}";

    private static string QueueKey(string name) => $"nexjob:queue:{name}:z";

    private static string RecurringKey(string id) => $"nexjob:recurring:{id}";

    private static string IdempotencyRedisKey(string key) => $"nexjob:idempotency:{key}";

    private static string LogsKey(string id) => $"nexjob:logs:{id}";

    private static string ContinuationSetKey(Guid parentId) => $"nexjob:continuations:{parentId}";

    private static string ServerKey(string id) => $"nexjob:servers:{id}";

    // priority (1-4) * 10^13 + ticks — lower score = higher priority
    private static double QueueScore(int priority, DateTimeOffset createdAt) =>
        (priority * 10_000_000_000_000.0) + createdAt.UtcTicks;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;

    private static bool MatchesFilter(JobRecord record, JobFilter filter)
    {
        if (filter.Status.HasValue && record.Status != filter.Status.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.Queue) && !string.Equals(record.Queue, filter.Queue, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filter.RecurringJobId) && !string.Equals(record.RecurringJobId, filter.RecurringJobId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            if (!record.JobType.Contains(s, StringComparison.OrdinalIgnoreCase) &&
                !record.Id.Value.ToString().Contains(s, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static HashEntry[] BuildJobHash(JobRecord job) =>
    [
        new("id", job.Id.Value.ToString()),
        new("jobType", job.JobType),
        new("inputType", job.InputType),
        new("inputJson", job.InputJson),
        new("schemaVersion", job.SchemaVersion),
        new("queue", job.Queue),
        new("priority", (int)job.Priority),
        new("status", job.Status.ToString()),
        new("idempotencyKey", job.IdempotencyKey ?? string.Empty),
        new("attempts", job.Attempts),
        new("maxAttempts", job.MaxAttempts),
        new("createdAt", job.CreatedAt.ToString("O", CultureInfo.InvariantCulture)),
        new("scheduledAt", job.ScheduledAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty),
        new("processingStartedAt", job.ProcessingStartedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty),
        new("heartbeatAt", job.HeartbeatAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty),
        new("completedAt", job.CompletedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty),
        new("retryAt", job.RetryAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty),
        new("exceptionMessage", job.LastErrorMessage ?? string.Empty),
        new("exceptionStackTrace", job.LastErrorStackTrace ?? string.Empty),
        new("parentJobId", job.ParentJobId?.Value.ToString() ?? string.Empty),
        new("recurringJobId", job.RecurringJobId ?? string.Empty),
        new("executionLogs", string.Empty),
        new("tags", JsonSerializer.Serialize(job.Tags, JsonOpts)),
        new("progressPercent", job.ProgressPercent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
        new("progressMessage", job.ProgressMessage ?? string.Empty),
    ];

    private static bool IsActiveState(JobStatus status) =>
        status is JobStatus.Enqueued or JobStatus.Processing or JobStatus.Scheduled or JobStatus.AwaitingContinuation;

    private static JobStatus GetStatusFromHash(HashEntry[] hash)
    {
        var statusEntry = Array.Find(hash, e => e.Name == "status");
        if (statusEntry.Name.IsNull)
        {
            return JobStatus.Failed;
        }

        var statusStr = statusEntry.Value.ToString();
        if (Enum.TryParse<JobStatus>(statusStr, out var parsed))
        {
            return parsed;
        }

        return JobStatus.Failed;
    }

    private static Dictionary<string, string> ParseHash(HashEntry[] entries) =>
        entries.ToDictionary(e => e.Name.ToString(), e => e.Value.ToString(), StringComparer.Ordinal);

    private static Dictionary<string, string> ParseFlatArray(RedisValue[] flatArray)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < flatArray.Length; i += 2)
        {
            dict[flatArray[i].ToString()] = flatArray[i + 1].ToString();
        }

        return dict;
    }

    private static JobRecord HashToRecord(Dictionary<string, string> d)
    {
        DateTimeOffset? ParseDate(string key) =>
            d.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) &&
            DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var r)
                ? r : null;

        var logsJson = d.GetValueOrDefault("executionLogs", string.Empty);
        IReadOnlyList<JobExecutionLog> logs;
        if (string.IsNullOrEmpty(logsJson))
        {
            logs = Array.Empty<JobExecutionLog>();
        }
        else
        {
            var deserialized = JsonSerializer.Deserialize<List<JobExecutionLog>>(logsJson, JsonOpts);
            logs = deserialized is not null
                ? (IReadOnlyList<JobExecutionLog>)deserialized
                : Array.Empty<JobExecutionLog>();
        }

        var priorityRaw = d.GetValueOrDefault("priority", "3");
        JobPriority priority;
        if (int.TryParse(priorityRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var priInt))
        {
            priority = (JobPriority)priInt;
        }
        else
        {
            priority = Enum.TryParse<JobPriority>(priorityRaw, out var priEnum) ? priEnum : JobPriority.Normal;
        }

        return new JobRecord
        {
            Id = new JobId(Guid.Parse(d["id"])),
            JobType = d.GetValueOrDefault("jobType", string.Empty),
            InputType = d.GetValueOrDefault("inputType", string.Empty),
            InputJson = d.GetValueOrDefault("inputJson", string.Empty),
            SchemaVersion = int.TryParse(d.GetValueOrDefault("schemaVersion"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sv) ? sv : 1,
            Queue = d.GetValueOrDefault("queue", "default"),
            Priority = priority,
            Status = Enum.Parse<JobStatus>(d.GetValueOrDefault("status", "Enqueued")),
            IdempotencyKey = NullIfEmpty(d.GetValueOrDefault("idempotencyKey")),
            Attempts = int.TryParse(d.GetValueOrDefault("attempts"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var att) ? att : 0,
            MaxAttempts = int.TryParse(d.GetValueOrDefault("maxAttempts"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ma) ? ma : 10,
            CreatedAt = DateTimeOffset.Parse(d["createdAt"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            ScheduledAt = ParseDate("scheduledAt"),
            ProcessingStartedAt = ParseDate("processingStartedAt"),
            HeartbeatAt = ParseDate("heartbeatAt"),
            CompletedAt = ParseDate("completedAt"),
            RetryAt = ParseDate("retryAt"),
            LastErrorMessage = NullIfEmpty(d.GetValueOrDefault("exceptionMessage")),
            LastErrorStackTrace = NullIfEmpty(d.GetValueOrDefault("exceptionStackTrace")),
            ParentJobId = Guid.TryParse(d.GetValueOrDefault("parentJobId"), out var pg) ? new JobId(pg) : null,
            RecurringJobId = NullIfEmpty(d.GetValueOrDefault("recurringJobId")),
            ExecutionLogs = logs,
            Tags = DeserializeTags(d.GetValueOrDefault("tags", string.Empty)),
            ProgressPercent = int.TryParse(d.GetValueOrDefault("progressPercent"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pp) ? pp : null,
            ProgressMessage = NullIfEmpty(d.GetValueOrDefault("progressMessage")),
        };
    }

    private static IReadOnlyList<string> DeserializeTags(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<string>>(json, JsonOpts) ?? [];
    }

    private static RecurringJobRecord HashToRecurring(Dictionary<string, string> d)
    {
        DateTimeOffset? ParseDate(string key) =>
            d.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) &&
            DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var r)
                ? r : null;

        var cp = Enum.TryParse<RecurringConcurrencyPolicy>(
            d.GetValueOrDefault("concurrencyPolicy"), out var cpVal)
            ? cpVal
            : RecurringConcurrencyPolicy.SkipIfRunning;

        var createdAt = DateTimeOffset.TryParse(
            d.GetValueOrDefault("createdAt"), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var ca)
            ? ca
            : DateTimeOffset.UtcNow;

        return new RecurringJobRecord
        {
            RecurringJobId = d["recurringJobId"],
            JobType = d.GetValueOrDefault("jobType", string.Empty),
            InputType = d.GetValueOrDefault("inputType", string.Empty),
            InputJson = d.GetValueOrDefault("inputJson", string.Empty),
            Cron = d.GetValueOrDefault("cron", string.Empty),
            TimeZoneId = NullIfEmpty(d.GetValueOrDefault("timeZoneId")),
            Queue = d.GetValueOrDefault("queue", "default"),
            NextExecution = ParseDate("nextExecution"),
            LastExecutedAt = ParseDate("lastExecution"),
            LastExecutionStatus = Enum.TryParse<JobStatus>(d.GetValueOrDefault("lastExecutionStatus"), out var ls) ? ls : null,
            LastExecutionError = NullIfEmpty(d.GetValueOrDefault("lastExecutionError")),
            ConcurrencyPolicy = cp,
            CreatedAt = createdAt,
            CronOverride = NullIfEmpty(d.GetValueOrDefault("cronOverride")),
            Enabled = string.Equals(d.GetValueOrDefault("enabled", "true"), "true", StringComparison.Ordinal),
            DeletedByUser = string.Equals(d.GetValueOrDefault("deletedByUser", "false"), "true", StringComparison.Ordinal),
        };
    }

    // ── Private instance helpers ──────────────────────────────────────────────

    private async IAsyncEnumerable<RedisKey> ScanJobKeysAsync()
    {
        var endpoints = _db.Multiplexer.GetEndPoints();
        var server = _db.Multiplexer.GetServer(endpoints[0]);
        await foreach (var key in server.KeysAsync(database: _db.Database, pattern: "nexjob:jobs:*").ConfigureAwait(false))
        {
            yield return key;
        }
    }

    private async Task PromoteScheduledJobsAsync()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var dueMembers = await _db.SortedSetRangeByScoreWithScoresAsync(
            ScheduledKey, double.NegativeInfinity, nowMs).ConfigureAwait(false);

        foreach (var member in dueMembers)
        {
            var id = member.Element.ToString();
            var jobHash = await _db.HashGetAllAsync(JobKey(id)).ConfigureAwait(false);
            if (jobHash.Length == 0)
            {
                await _db.SortedSetRemoveAsync(ScheduledKey, id).ConfigureAwait(false);
                continue;
            }

            var dict = ParseHash(jobHash);
            var queue = dict.GetValueOrDefault("queue", "default");
            var priorityStr = dict.GetValueOrDefault("priority", "3");
            var createdAtStr = dict.GetValueOrDefault("createdAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            var priority = int.TryParse(priorityStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 3;
            var createdAt = DateTimeOffset.TryParse(createdAtStr, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var ca) ? ca : DateTimeOffset.UtcNow;

            await _db.HashSetAsync(JobKey(id), "status", "Enqueued").ConfigureAwait(false);
            await _db.SortedSetRemoveAsync(ScheduledKey, id).ConfigureAwait(false);
            await _db.SortedSetAddAsync(QueueKey(queue), id, QueueScore(priority, createdAt)).ConfigureAwait(false);
        }
    }
}
