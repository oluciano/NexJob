using System.Globalization;
using StackExchange.Redis;

namespace NexJob.Redis;

/// <summary>
/// Redis-backed implementation of <see cref="IDistributedThrottleStore"/> using Lua scripts
/// for atomic acquire and release operations.
/// </summary>
internal sealed class RedisDistributedThrottleStore : IDistributedThrottleStore
{
    private static readonly LuaScript AcquireScript = LuaScript.Prepare(@"
        local key = KEYS[1]
        local max = tonumber(ARGV[1])
        local ttl = tonumber(ARGV[2])

        local current = tonumber(redis.call('GET', key) or '0')
        if current >= max then
          return 0
        end
        redis.call('INCR', key)
        redis.call('EXPIRE', key, ttl)
        return 1
    ");

    private static readonly LuaScript ReleaseScript = LuaScript.Prepare(@"
        local key = KEYS[1]
        local current = tonumber(redis.call('GET', key) or '0')
        if current > 0 then
          redis.call('DECR', key)
        end
        return 1
    ");

    private readonly IDatabase _db;
    private readonly int _ttlSeconds;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisDistributedThrottleStore"/> class.
    /// </summary>
    /// <param name="db">The redis database.</param>
    /// <param name="options">The NexJob options.</param>
    public RedisDistributedThrottleStore(IDatabase db, NexJobOptions options)
    {
        _db = db;
        _ttlSeconds = (int)options.DistributedThrottleTtl.TotalSeconds;
    }

    /// <inheritdoc/>
    public async Task<bool> TryAcquireAsync(string resource, int maxConcurrent, CancellationToken ct = default)
    {
        var key = $"nexjob:throttle:{resource}";

        var result = await _db.ScriptEvaluateAsync(
            AcquireScript.ExecutableScript,
            [(RedisKey)key],
            [(RedisValue)maxConcurrent.ToString(CultureInfo.InvariantCulture), (RedisValue)_ttlSeconds.ToString(CultureInfo.InvariantCulture)]).ConfigureAwait(false);

        return (int)result == 1;
    }

    /// <inheritdoc/>
    public async Task ReleaseAsync(string resource, CancellationToken ct = default)
    {
        var key = $"nexjob:throttle:{resource}";
        await _db.ScriptEvaluateAsync(
            ReleaseScript.ExecutableScript,
            [(RedisKey)key],
            Array.Empty<RedisValue>()).ConfigureAwait(false);
    }
}
