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
    private const int PromotionBatchSize = 100;
    private const int MaxPromotionRounds = 5;
    private const string RecurringAllKey = "nexjob:recurring:all";
    private const string ThroughputKey = "nexjob:throughput";
    private const string SucceededSetKey = "nexjob:status:Succeeded";
    private const string FailedSetKey = "nexjob:status:Failed";
    private const string QueueKeyPrefix = "nexjob:queue:";
    private const string QueueKeySuffix = ":z";
    private const string ServersAllKey = "nexjob:servers:all";

    // Lua function shared by every script that finishes a job successfully: moves each child still waiting on the
    // parent into its queue in the same atomic step and deletes the continuation set.
    private const string ReleaseContinuationsFunction =
        """
        local function releaseContinuations(parentId, nowTicks)
          local contKey = 'nexjob:continuations:' .. parentId
          local continuations = redis.call('SMEMBERS', contKey)
          for i = 1, #continuations do
            local childId = continuations[i]
            local childKey = 'nexjob:jobs:' .. childId
            if redis.call('HGET', childKey, 'status') == 'AwaitingContinuation' then
              redis.call('HSET', childKey, 'status', 'Enqueued', 'scheduledAt', '')
              local score = tonumber(redis.call('HGET', childKey, 'queueScore'))
              if not score then
                -- Legacy child written before queueScore existed: derive it from priority and now
                score = (tonumber(redis.call('HGET', childKey, 'priority')) or 3) * 10000000000000 + tonumber(nowTicks)
              end
              local queue = redis.call('HGET', childKey, 'queue')
              if not queue or queue == '' then
                queue = 'default'
              end
              redis.call('ZADD', 'nexjob:queue:' .. queue .. ':z', score, childId)
            end
          end
          redis.call('DEL', contKey)
        end

        """;

    private static readonly JsonSerializerOptions JsonOpts = new();

    /// <summary>
    /// Requeues (or fails, when attempts are exhausted) one orphaned job atomically. ARGV: job id, the heartbeat the
    /// scan read, now (ISO 8601), queue name, queue score. Returns 0 when skipped (heartbeat refreshed or entry gone),
    /// 1 requeued, 2 stale entry of a job that is no longer Processing (job untouched), 3 job hash missing, 4 failed.
    /// </summary>
    private static readonly LuaScript RequeueOrphanScript = LuaScript.Prepare(
        """
        local id = ARGV[1]
        local jobKey = 'nexjob:jobs:' .. id
        local current = redis.call('HGET', 'nexjob:processing', id)

        -- Entry gone, or heartbeat refreshed since the scan read it: the worker is alive or the job was handled.
        if not current or current ~= ARGV[2] then
          return 0
        end

        if redis.call('EXISTS', jobKey) == 0 then
          redis.call('HDEL', 'nexjob:processing', id)
          return 3
        end

        -- A finished job can still have a stale processing entry: clean the entry, never touch the job.
        if redis.call('HGET', jobKey, 'status') ~= 'Processing' then
          redis.call('HDEL', 'nexjob:processing', id)
          return 2
        end

        local attempts = tonumber(redis.call('HGET', jobKey, 'attempts')) or 0
        local maxAttempts = tonumber(redis.call('HGET', jobKey, 'maxAttempts')) or 10

        if attempts >= maxAttempts then
          local message = redis.call('HGET', jobKey, 'exceptionMessage')
          if not message or message == '' then
            message = 'Orphaned execution exceeded maximum attempts.'
          end

          redis.call('HSET', jobKey, 'status', 'Failed', 'heartbeatAt', '', 'processingStartedAt', '',
                     'completedAt', ARGV[3], 'exceptionMessage', message)
          redis.call('HDEL', 'nexjob:processing', id)
          redis.call('ZADD', 'nexjob:status:Failed', tonumber(ARGV[6]), id)
          return 4
        end

        redis.call('HSET', jobKey, 'status', 'Enqueued', 'heartbeatAt', '', 'processingStartedAt', '')
        redis.call('HDEL', 'nexjob:processing', id)
        redis.call('ZADD', 'nexjob:queue:' .. ARGV[4] .. ':z', tonumber(ARGV[5]), id)
        return 1
        """);

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

    private static readonly LuaScript FetchBatchScript = LuaScript.Prepare(
        """
        local maxBatch = tonumber(ARGV[1])
        local now = ARGV[2]
        local fetched = {}

        for i = 1, #KEYS do
          local zkey = KEYS[i]
          local remaining = maxBatch - #fetched
          if remaining <= 0 then
            break
          end

          local members = redis.call('ZRANGE', zkey, 0, remaining - 1)
          for j = 1, #members do
            local id = members[j]
            redis.call('ZREM', zkey, id)
            local jobKey = 'nexjob:jobs:' .. id
            redis.call('HSET', jobKey,
              'status', 'Processing',
              'processingStartedAt', now,
              'heartbeatAt', now)
            redis.call('HINCRBY', jobKey, 'attempts', 1)
            redis.call('HSET', 'nexjob:processing', id, now)
            table.insert(fetched, redis.call('HGETALL', jobKey))
          end
        end

        return fetched
        """);

    private static readonly LuaScript ReleaseContinuationsScript = LuaScript.Prepare(
        ReleaseContinuationsFunction + "releaseContinuations(ARGV[1], ARGV[2])\nreturn 1");

    private static readonly LuaScript AcknowledgeBatchScript = LuaScript.Prepare(
        ReleaseContinuationsFunction + """
        local nowIso = ARGV[1]
        local nowMs = tonumber(ARGV[2])
        local nowTicks = ARGV[3]

        for i = 4, #ARGV do
          local id = ARGV[i]
          local jobKey = 'nexjob:jobs:' .. id
          redis.call('HSET', jobKey, 'status', 'Succeeded', 'completedAt', nowIso, 'heartbeatAt', '')
          redis.call('HDEL', 'nexjob:processing', id)
          redis.call('ZADD', 'nexjob:throughput', nowMs, id)
          redis.call('ZADD', 'nexjob:status:Succeeded', nowMs, id)
          releaseContinuations(id, nowTicks)
        end

        return 1
        """);

    private static readonly LuaScript CommitJobResultScript = LuaScript.Prepare(
        ReleaseContinuationsFunction + """
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
          redis.call('ZADD', 'nexjob:status:Succeeded', tonumber(ARGV[10]), ARGV[1])

          releaseContinuations(ARGV[1], ARGV[9])

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
                       'exceptionMessage', ARGV[6], 'exceptionStackTrace', ARGV[7],
                       'heartbeatAt', '', 'retryAt', '')
            redis.call('HDEL', 'nexjob:processing', ARGV[1])
            redis.call('ZADD', 'nexjob:status:Failed', tonumber(ARGV[10]), ARGV[1])

            -- Update recurring job if applicable and no retry
            if ARGV[4] ~= '' then
              local rKey = 'nexjob:recurring:' .. ARGV[4]
              redis.call('HSET', rKey, 'lastExecutionStatus', 'Failed', 'lastExecutionError', ARGV[6], 'updatedAt', ARGV[3])
            end
          end
        end

        return 1
        """);

    // Promotes due scheduled jobs into their queues atomically. ZREM runs first so two nodes can never
    // promote the same entry, and only jobs still Scheduled are moved (stale entries are just dropped).
    private static readonly LuaScript PromoteScheduledScript = LuaScript.Prepare(
        """
        local due = redis.call('ZRANGEBYSCORE', 'nexjob:scheduled', '-inf', ARGV[1], 'LIMIT', 0, tonumber(ARGV[2]))
        for i = 1, #due do
          local id = due[i]
          redis.call('ZREM', 'nexjob:scheduled', id)
          local jobKey = 'nexjob:jobs:' .. id
          if redis.call('HGET', jobKey, 'status') == 'Scheduled' then
            redis.call('HSET', jobKey, 'status', 'Enqueued')
            local score = tonumber(redis.call('HGET', jobKey, 'queueScore'))
            if not score then
              -- Legacy job written before queueScore existed: derive it from priority and now
              score = (tonumber(redis.call('HGET', jobKey, 'priority')) or 3) * 10000000000000 + tonumber(ARGV[3])
            end
            local queue = redis.call('HGET', jobKey, 'queue')
            if not queue or queue == '' then
              queue = 'default'
            end
            redis.call('ZADD', 'nexjob:queue:' .. queue .. ':z', score, id)
          end
        end
        return #due
        """);

    // Deletes the idempotency key only while it still points at the given job, so releasing the key
    // of a purged job never removes the key of a newer job that re-used it.
    private static readonly LuaScript ReleaseIdempotencyScript = LuaScript.Prepare(
        """
        if redis.call('GET', ARGV[1]) == ARGV[2] then
          return redis.call('DEL', ARGV[1])
        end
        return 0
        """);

    private static readonly LuaScript EnqueueScript = LuaScript.Prepare(
        """
        local idemKey = ARGV[1]
        local jobId = ARGV[2]
        local targetKind = ARGV[3]
        local targetKey = ARGV[4]
        local targetScore = ARGV[5]
        local jobKey = 'nexjob:jobs:' .. jobId

        -- Only check idempotency if a key was provided
        if idemKey ~= '' then
          local existingId = redis.call('GET', idemKey)
          if existingId then
            return { 'EXISTS', existingId }
          end
        end

        -- Set job hash fields (ARGV[6], ARGV[7], ...)
        local hashArgs = {}
        for i = 6, #ARGV do
          table.insert(hashArgs, ARGV[i])
        end

        if #hashArgs > 0 then
          redis.call('HSET', jobKey, unpack(hashArgs))
        end

        -- The key has no TTL: it lives as long as the job and is released when the job is deleted
        if idemKey ~= '' then
          redis.call('SET', idemKey, jobId)
        end

        -- Insert into queue / scheduled set / continuation set in the same atomic step
        if targetKind == 'zset' then
          redis.call('ZADD', targetKey, tonumber(targetScore), jobId)
        elseif targetKind == 'set' then
          redis.call('SADD', targetKey, jobId)
        end

        return { 'NEW', jobId }
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
        var id = job.Id.Value.ToString();
        var idemKey = job.IdempotencyKey is not null ? IdempotencyRedisKey(job.IdempotencyKey) : string.Empty;

        var hashFields = BuildJobHash(job);
        var hashArgs = new List<RedisValue>();
        foreach (var field in hashFields)
        {
            hashArgs.Add(field.Name.ToString());
            hashArgs.Add(field.Value.ToString());
        }

        // Queue insertion is part of the script so hash + queue entry are created atomically.
        string targetKind;
        string targetKey;
        string targetScore;
        if (job.Status == JobStatus.AwaitingContinuation && job.ParentJobId.HasValue)
        {
            targetKind = "set";
            targetKey = ContinuationSetKey(job.ParentJobId.Value.Value);
            targetScore = string.Empty;
        }
        else if (job.Status == JobStatus.Scheduled && job.ScheduledAt.HasValue)
        {
            targetKind = "zset";
            targetKey = ScheduledKey;
            targetScore = job.ScheduledAt.Value.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        }
        else if (job.Status == JobStatus.Enqueued)
        {
            targetKind = "zset";
            targetKey = QueueKey(job.Queue);
            targetScore = QueueScore((int)job.Priority, job.CreatedAt).ToString("R", CultureInfo.InvariantCulture);
        }
        else
        {
            targetKind = string.Empty;
            targetKey = string.Empty;
            targetScore = string.Empty;
        }

        var scriptArgs = new RedisValue[]
        {
            idemKey,
            id,
            targetKind,
            targetKey,
            targetScore,
        };
        var combinedArgs = scriptArgs.Concat(hashArgs).ToArray();

        // Loop allows one retry after deleting a stale idempotency key.
        // Maximum two iterations: initial attempt + one retry after key deletion.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var rawResult = await _db.ScriptEvaluateAsync(
                EnqueueScript.ExecutableScript, keys: null, values: combinedArgs)
                .ConfigureAwait(false);

            if (!rawResult.IsNull)
            {
                var resultArray = (RedisValue[])rawResult!;
                if (resultArray.Length == 2)
                {
                    var resultType = resultArray[0].ToString();
                    var returnedId = resultArray[1].ToString();

                    if (string.Equals(resultType, "EXISTS", StringComparison.Ordinal))
                    {
                        var existingJobId = new JobId(Guid.Parse(returnedId!));
                        var jobHash = await _db.HashGetAllAsync(JobKey(returnedId!)).ConfigureAwait(false);
                        var existingStatus = GetStatusFromHash(jobHash);

                        var enqueueResult = ResolveDuplicate(existingJobId, existingStatus, duplicatePolicy);

                        if (enqueueResult.WasRejected || IsActiveState(existingStatus))
                        {
                            return enqueueResult;
                        }

                        // Policy allows re-enqueue: delete stale key and retry once
                        if (!string.IsNullOrEmpty(idemKey))
                        {
                            await _db.KeyDeleteAsync(idemKey).ConfigureAwait(false);
                        }

                        continue; // retry the loop
                    }
                }
            }

            break; // new job created — proceed to queue/schedule
        }

        // New job (hash, idempotency key and queue entry) was created atomically by the script.
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
    public async Task<IReadOnlyList<JobRecord>> FetchBatchAsync(
        IReadOnlyList<string> queues,
        int maxBatchSize,
        CancellationToken cancellationToken = default)
    {
        if (queues.Count == 0 || maxBatchSize <= 0)
        {
            return Array.Empty<JobRecord>();
        }

        if (maxBatchSize == 1)
        {
            var single = await FetchNextAsync(queues, cancellationToken).ConfigureAwait(false);
            return single is not null ? new[] { single } : Array.Empty<JobRecord>();
        }

        await PromoteScheduledJobsAsync().ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var keys = queues.Select(q => (RedisKey)QueueKey(q)).ToArray();
        var args = new RedisValue[] { maxBatchSize.ToString(CultureInfo.InvariantCulture), now };

        var rawResult = await _db.ScriptEvaluateAsync(FetchBatchScript.ExecutableScript, keys, args).ConfigureAwait(false);
        if (rawResult.IsNull)
        {
            return Array.Empty<JobRecord>();
        }

        var resultArrays = (RedisResult[])rawResult!;
        if (resultArrays.Length == 0)
        {
            return Array.Empty<JobRecord>();
        }

        var list = new List<JobRecord>(resultArrays.Length);
        for (var i = 0; i < resultArrays.Length; i++)
        {
            var items = (RedisValue[])resultArrays[i]!;
            if (items.Length > 0)
            {
                list.Add(HashToRecord(ParseFlatArray(items)));
            }
        }

        return list;
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

        await _db.HashDeleteAsync(JobKey(id), "checkpointJson").ConfigureAwait(false);
        await _db.HashDeleteAsync(ProcessingKey, id).ConfigureAwait(false);
        await _db.SortedSetAddAsync(ThroughputKey, id, now.ToUnixTimeMilliseconds()).ConfigureAwait(false);
        await _db.SortedSetAddAsync(SucceededSetKey, id, now.ToUnixTimeMilliseconds()).ConfigureAwait(false);
        await _db.ScriptEvaluateAsync(
            ReleaseContinuationsScript.ExecutableScript,
            keys: null,
            values: [id, now.UtcTicks.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task AcknowledgeBatchAsync(IReadOnlyList<JobId> jobIds, CancellationToken cancellationToken = default)
    {
        if (jobIds.Count == 0)
        {
            return;
        }

        if (jobIds.Count == 1)
        {
            await AcknowledgeAsync(jobIds[0], cancellationToken).ConfigureAwait(false);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var nowIso = now.ToString("O", CultureInfo.InvariantCulture);
        var nowMs = now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

        var args = new RedisValue[3 + jobIds.Count];
        args[0] = nowIso;
        args[1] = nowMs;
        args[2] = now.UtcTicks.ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < jobIds.Count; i++)
        {
            args[3 + i] = jobIds[i].Value.ToString();
        }

        await _db.ScriptEvaluateAsync(AcknowledgeBatchScript.ExecutableScript, Array.Empty<RedisKey>(), args).ConfigureAwait(false);
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
            await _db.SortedSetAddAsync(FailedSetKey, id, now.ToUnixTimeMilliseconds()).ConfigureAwait(false);
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
            var heartbeatText = entry.Value.ToString();
            if (!DateTimeOffset.TryParse(heartbeatText, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var heartbeat))
            {
                continue;
            }

            if (heartbeat >= cutoff)
            {
                continue;
            }

            // Queue, priority and creation time do not change while a job is Processing, so reading them here is
            // safe. Everything that can change (status, heartbeat, attempts) is re-checked inside the script.
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

            await RunRequeueOrphanScriptAsync(_db, id, heartbeatText, queue, QueueScore(priority, createdAt)).ConfigureAwait(false);
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
        // Counts come from structures that already exist per state (queue sets, processing hash, scheduled set,
        // and the Succeeded/Failed sets), so the cost does not grow with the number of stored jobs.
        var enqueued = 0L;
        foreach (var queueKey in await GetQueueKeysAsync().ConfigureAwait(false))
        {
            enqueued += await _db.SortedSetLengthAsync(queueKey).ConfigureAwait(false);
        }

        var processing = await _db.HashLengthAsync(ProcessingKey).ConfigureAwait(false);
        var scheduled = await _db.SortedSetLengthAsync(ScheduledKey).ConfigureAwait(false);
        var succeeded = await _db.SortedSetLengthAsync(SucceededSetKey).ConfigureAwait(false);
        var failed = await _db.SortedSetLengthAsync(FailedSetKey).ConfigureAwait(false);

        var recentFailures = new List<JobRecord>();
        var recentFailedIds = await _db.SortedSetRangeByRankAsync(FailedSetKey, 0, 9, Order.Descending).ConfigureAwait(false);
        foreach (var failedId in recentFailedIds)
        {
            var hash = await _db.HashGetAllAsync(JobKey(failedId.ToString())).ConfigureAwait(false);
            if (hash.Length > 0)
            {
                recentFailures.Add(HashToRecord(ParseHash(hash)));
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
            Enqueued = (int)enqueued,
            Processing = (int)processing,
            Succeeded = (int)succeeded,
            Failed = (int)failed,
            Scheduled = (int)scheduled,
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
        var idempotencyKey = (string?)await _db.HashGetAsync(JobKey(idStr), "idempotencyKey").ConfigureAwait(false);
        await _db.KeyDeleteAsync(JobKey(idStr)).ConfigureAwait(false);
        await _db.KeyDeleteAsync(LogsKey(idStr)).ConfigureAwait(false);
        await _db.HashDeleteAsync(ProcessingKey, idStr).ConfigureAwait(false);
        await RemoveFromStatusSetsAsync(_db, idStr).ConfigureAwait(false);
        await ReleaseIdempotencyKeyAsync(_db, idempotencyKey, idStr).ConfigureAwait(false);
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
        await RemoveFromStatusSetsAsync(_db, idStr).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<QueueMetrics>> GetQueueMetricsAsync(
        CancellationToken cancellationToken = default)
    {
        var metrics = new Dictionary<string, (int Enqueued, int Processing)>(StringComparer.Ordinal);

        foreach (var queueKey in await GetQueueKeysAsync().ConfigureAwait(false))
        {
            var name = queueKey[QueueKeyPrefix.Length..^QueueKeySuffix.Length];
            metrics[name] = ((int)await _db.SortedSetLengthAsync(queueKey).ConfigureAwait(false), 0);
        }

        // Running jobs: bounded by the number of workers, not by how many jobs are stored.
        var processingIds = await _db.HashKeysAsync(ProcessingKey).ConfigureAwait(false);
        foreach (var processingId in processingIds)
        {
            var queue = (string?)await _db.HashGetAsync(JobKey(processingId.ToString()), "queue").ConfigureAwait(false);
            if (string.IsNullOrEmpty(queue))
            {
                continue;
            }

            metrics.TryGetValue(queue, out var current);
            metrics[queue] = (current.Enqueued, current.Processing + 1);
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
            DateTimeOffset.UtcNow.UtcTicks.ToString(CultureInfo.InvariantCulture),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
        };

        // Execute atomic state transitions via Lua script
        await _db.ScriptEvaluateAsync(CommitJobResultScript.ExecutableScript, keys: null, values: args).ConfigureAwait(false);

        if (result.Succeeded && result.PurgeOnSuccess)
        {
            var idempotencyKey = (string?)await _db.HashGetAsync(JobKey(idStr), "idempotencyKey").ConfigureAwait(false);
            await _db.KeyDeleteAsync(JobKey(idStr)).ConfigureAwait(false);
            await _db.KeyDeleteAsync(LogsKey(idStr)).ConfigureAwait(false);
            await ReleaseIdempotencyKeyAsync(_db, idempotencyKey, idStr).ConfigureAwait(false);
        }
        else
        {
            if (result.Succeeded)
            {
                await _db.HashDeleteAsync(JobKey(idStr), "checkpointJson").ConfigureAwait(false);

                if (result.TrimPayloadOnSuccess)
                {
                    await _db.HashSetAsync(JobKey(idStr), "inputJson", string.Empty).ConfigureAwait(false);
                }
            }

            // Persist logs (non-critical for atomicity)
            await _db.StringSetAsync(LogsKey(idStr), logsJson).ConfigureAwait(false);
            await _db.HashSetAsync(JobKey(idStr), "executionLogs", logsJson).ConfigureAwait(false);
        }
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
    public async Task SaveCheckpointAsync(
        JobId jobId, string checkpointJson, int? percent, string? message, CancellationToken ct = default)
    {
        var key = (RedisKey)JobKey(jobId.Value.ToString());
        var entries = new List<HashEntry>
        {
            new HashEntry("checkpointJson", checkpointJson),
        };

        if (percent.HasValue)
        {
            entries.Add(new HashEntry("progressPercent", percent.Value.ToString(CultureInfo.InvariantCulture)));
        }

        if (message is not null)
        {
            entries.Add(new HashEntry("progressMessage", message));
        }

        await _db.HashSetAsync(key, entries.ToArray()).ConfigureAwait(false);
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

    /// <inheritdoc/>
    public async Task<IReadOnlyList<JobCatalogItem>> GetJobCatalogAsync(CancellationToken cancellationToken = default)
    {
        var grouped = new Dictionary<(string JobType, string Queue), List<JobRecord>>();

        await foreach (var key in ScanJobKeysAsync().WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var hash = await _db.HashGetAllAsync(key).ConfigureAwait(false);
            if (hash.Length == 0)
            {
                continue;
            }

            var d = ParseHash(hash);
            var record = HashToRecord(d);
            var groupKey = (record.JobType, record.Queue);

            if (!grouped.TryGetValue(groupKey, out var list))
            {
                list = new List<JobRecord>();
                grouped[groupKey] = list;
            }

            list.Add(record);
        }

        var items = new List<JobCatalogItem>();

        foreach (var (key, jobs) in grouped)
        {
            var succeeded = jobs.Count(j => j.Status == JobStatus.Succeeded);
            var failed = jobs.Count(j => j.Status == JobStatus.Failed);
            var total = jobs.Count;

            var lastExecuted = jobs
                .Select(j => j.CompletedAt ?? j.ProcessingStartedAt)
                .Where(t => t.HasValue)
                .OrderByDescending(t => t!.Value)
                .FirstOrDefault();

            var durations = jobs
                .Where(j => j.ProcessingStartedAt.HasValue && j.CompletedAt.HasValue && j.CompletedAt >= j.ProcessingStartedAt)
                .Select(j => (j.CompletedAt!.Value - j.ProcessingStartedAt!.Value).TotalSeconds)
                .ToList();

            double? avgDuration = durations.Count > 0 ? durations.Average() : null;

            items.Add(new JobCatalogItem(
                JobType: key.JobType,
                Queue: key.Queue,
                TotalRuns: total,
                SucceededRuns: succeeded,
                FailedRuns: failed,
                LastExecutedAt: lastExecuted,
                AvgDurationSeconds: avgDuration));
        }

        return items
            .OrderBy(i => i.JobType, StringComparer.Ordinal)
            .ThenBy(i => i.Queue, StringComparer.Ordinal)
            .ToList();
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
        var batchSize = policy.BatchSize > 0 ? policy.BatchSize : 1000;
        var keysToDelete = new List<RedisKey>(batchSize);
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
            var statusStr = dict.GetValueOrDefault("status");
            var isDeadLetter = string.Equals(statusStr, "DeadLetter", StringComparison.OrdinalIgnoreCase);

            DateTimeOffset? cutoff = null;
            TimeSpan retention = TimeSpan.Zero;

            if (isDeadLetter && policy.RetainDeadLetter > TimeSpan.Zero)
            {
                var completedAtStr = dict.GetValueOrDefault("completedAt") ?? dict.GetValueOrDefault("createdAt");
                if (completedAtStr != null
                    && DateTimeOffset.TryParse(completedAtStr, CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var dt))
                {
                    cutoff = dt;
                }

                retention = policy.RetainDeadLetter;
            }
            else
            {
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

                    case JobStatus.Failed:
                        {
                            if (policy.RetainFailed > TimeSpan.Zero)
                            {
                                var completedAtStr = dict.GetValueOrDefault("completedAt");
                                if (completedAtStr != null
                                    && DateTimeOffset.TryParse(completedAtStr, CultureInfo.InvariantCulture,
                                        DateTimeStyles.RoundtripKind, out var dt))
                                {
                                    cutoff = dt;
                                }

                                retention = policy.RetainFailed;
                            }
                            else if (policy.RetainDeadLetter > TimeSpan.Zero)
                            {
                                var completedAtStr = dict.GetValueOrDefault("completedAt") ?? dict.GetValueOrDefault("createdAt");
                                if (completedAtStr != null
                                    && DateTimeOffset.TryParse(completedAtStr, CultureInfo.InvariantCulture,
                                        DateTimeStyles.RoundtripKind, out var dt))
                                {
                                    cutoff = dt;
                                }

                                retention = policy.RetainDeadLetter;
                            }

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
            }

            if (cutoff.HasValue && now - cutoff.Value > retention)
            {
                keysToDelete.Add(key);
                if (keysToDelete.Count >= batchSize)
                {
                    deleted += await DeleteJobsWithLogsAsync(_db, keysToDelete).ConfigureAwait(false);
                    keysToDelete.Clear();
                    await Task.Yield();
                }
            }
        }

        if (keysToDelete.Count > 0)
        {
            deleted += await DeleteJobsWithLogsAsync(_db, keysToDelete).ConfigureAwait(false);
        }

        return deleted;
    }

    /// <summary>
    /// Runs the atomic orphan requeue for one job. Internal so the tests can exercise the guards directly.
    /// </summary>
    /// <returns>0 skipped, 1 requeued, 2 stale entry of a job that is no longer Processing, 3 job hash missing, 4 failed.</returns>
    internal static async Task<int> RunRequeueOrphanScriptAsync(
        IDatabase db, string id, string expectedHeartbeat, string queue, double queueScore)
    {
        var result = await db.ScriptEvaluateAsync(
            RequeueOrphanScript.ExecutableScript,
            keys: null,
            values:
            [
                id,
                expectedHeartbeat,
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                queue,
                queueScore.ToString("R", CultureInfo.InvariantCulture),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
            ]).ConfigureAwait(false);

        return (int)result;
    }

    // A job leaves the Succeeded/Failed sets when it is deleted, purged or requeued.
    private static async Task RemoveFromStatusSetsAsync(IDatabase db, string id)
    {
        await db.SortedSetRemoveAsync(SucceededSetKey, id).ConfigureAwait(false);
        await db.SortedSetRemoveAsync(FailedSetKey, id).ConfigureAwait(false);
    }

    // Deletes the job hashes and their separate logs keys; returns how many job hashes were actually removed.
    private static async Task<int> DeleteJobsWithLogsAsync(IDatabase db, List<RedisKey> jobKeys)
    {
        var jobKeyPrefix = JobKey(string.Empty);
        var logKeys = new RedisKey[jobKeys.Count];
        for (var i = 0; i < jobKeys.Count; i++)
        {
            logKeys[i] = LogsKey(jobKeys[i].ToString()[jobKeyPrefix.Length..]);
        }

        // Read the idempotency keys before the hashes are gone.
        var idemFields = await Task.WhenAll(jobKeys.Select(k => db.HashGetAsync(k, "idempotencyKey"))).ConfigureAwait(false);

        var removed = (int)await db.KeyDeleteAsync(jobKeys.ToArray()).ConfigureAwait(false);
        await db.KeyDeleteAsync(logKeys).ConfigureAwait(false);

        var members = jobKeys.Select(k => (RedisValue)k.ToString()[jobKeyPrefix.Length..]).ToArray();
        await db.SortedSetRemoveAsync(SucceededSetKey, members).ConfigureAwait(false);
        await db.SortedSetRemoveAsync(FailedSetKey, members).ConfigureAwait(false);

        for (var i = 0; i < jobKeys.Count; i++)
        {
            await ReleaseIdempotencyKeyAsync(db, (string?)idemFields[i], jobKeys[i].ToString()[jobKeyPrefix.Length..]).ConfigureAwait(false);
        }

        return removed;
    }

    // Releases the idempotency key of a deleted job; a no-op for jobs without a key or when a newer job owns it.
    private static async Task ReleaseIdempotencyKeyAsync(IDatabase db, string? idempotencyKey, string jobId)
    {
        if (string.IsNullOrEmpty(idempotencyKey))
        {
            return;
        }

        await db.ScriptEvaluateAsync(
            ReleaseIdempotencyScript.ExecutableScript,
            keys: null,
            values: [IdempotencyRedisKey(idempotencyKey), jobId]).ConfigureAwait(false);
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
        new("queueScore", QueueScore((int)job.Priority, job.CreatedAt).ToString("R", CultureInfo.InvariantCulture)),
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
            CheckpointJson = NullIfEmpty(d.GetValueOrDefault("checkpointJson")),
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

    // One sorted set exists per non-empty queue, so this scan is bounded by the number of queues.
    private async Task<List<string>> GetQueueKeysAsync()
    {
        var endpoints = _db.Multiplexer.GetEndPoints();
        var server = _db.Multiplexer.GetServer(endpoints[0]);
        var keys = new List<string>();
        await foreach (var key in server.KeysAsync(database: _db.Database, pattern: $"{QueueKeyPrefix}*{QueueKeySuffix}").ConfigureAwait(false))
        {
            keys.Add(key.ToString());
        }

        return keys;
    }

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
        // Bounded drain: each call promotes at most PromotionBatchSize entries inside one atomic script.
        for (var round = 0; round < MaxPromotionRounds; round++)
        {
            var now = DateTimeOffset.UtcNow;
            var args = new RedisValue[]
            {
                now.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                PromotionBatchSize,
                now.UtcTicks.ToString(CultureInfo.InvariantCulture),
            };

            var due = (long)await _db.ScriptEvaluateAsync(PromoteScheduledScript.ExecutableScript, keys: null, values: args).ConfigureAwait(false);
            if (due < PromotionBatchSize)
            {
                return;
            }
        }
    }
}
