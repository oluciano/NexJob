using System.Globalization;
using StackExchange.Redis;

namespace NexJob.Redis;

/// <summary>
/// Redis-backed implementation of <see cref="IDistributedThrottleStore"/>. Each occupied slot is one entry
/// (<c>holder id -&gt; expiry</c>) in a sorted set. The node that owns a slot keeps refreshing its expiry, so a slot
/// left behind by a crashed node is reclaimed after <c>3 x HeartbeatInterval</c> instead of lingering for an hour.
/// </summary>
internal sealed class RedisDistributedThrottleStore : IDistributedThrottleStore, IDisposable
{
    // ARGV: max, hold cap (seconds), holder id, holder ttl (ms). Expired holders are dropped before counting, using
    // the Redis clock so every node agrees on what has expired.
    private static readonly LuaScript AcquireScript = LuaScript.Prepare(@"
        local key = KEYS[1]
        local max = tonumber(ARGV[1])
        local capMs = tonumber(ARGV[2]) * 1000
        local holderId = ARGV[3]
        local holderTtlMs = tonumber(ARGV[4])

        local t = redis.call('TIME')
        local nowMs = t[1] * 1000 + math.floor(t[2] / 1000)

        redis.call('ZREMRANGEBYSCORE', key, '-inf', nowMs)
        if redis.call('ZCARD', key) >= max then
          return 0
        end

        redis.call('ZADD', key, nowMs + math.min(holderTtlMs, capMs), holderId)
        redis.call('PEXPIRE', key, capMs)
        return 1
    ");

    // ARGV: holder ttl (ms), holder ids...
    private static readonly LuaScript RefreshScript = LuaScript.Prepare(@"
        local key = KEYS[1]
        local holderTtlMs = tonumber(ARGV[1])
        local t = redis.call('TIME')
        local nowMs = t[1] * 1000 + math.floor(t[2] / 1000)
        for i = 2, #ARGV do
          redis.call('ZADD', key, nowMs + holderTtlMs, ARGV[i])
        end
        return 1
    ");

    private readonly IDatabase _db;
    private readonly int _capSeconds;
    private readonly TimeSpan _refreshInterval;
    private readonly long _holderTtlMs;
    private readonly Dictionary<string, List<Holder>> _held = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stop = new();
    private Task? _refresher;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisDistributedThrottleStore"/> class.
    /// </summary>
    /// <param name="db">The redis database.</param>
    /// <param name="options">The NexJob options.</param>
    public RedisDistributedThrottleStore(IDatabase db, NexJobOptions options)
    {
        _db = db;
        _capSeconds = (int)options.DistributedThrottleTtl.TotalSeconds;
        _refreshInterval = options.HeartbeatInterval;
        _holderTtlMs = (long)(options.HeartbeatInterval.TotalMilliseconds * 3);
    }

    /// <inheritdoc/>
    public async Task<bool> TryAcquireAsync(string resource, int maxConcurrent, CancellationToken ct = default)
    {
        var holderId = Guid.NewGuid().ToString("N");

        var result = await _db.ScriptEvaluateAsync(
            AcquireScript.ExecutableScript,
            [(RedisKey)HoldersKey(resource)],
            [
                (RedisValue)maxConcurrent.ToString(CultureInfo.InvariantCulture),
                (RedisValue)_capSeconds.ToString(CultureInfo.InvariantCulture),
                (RedisValue)holderId,
                (RedisValue)_holderTtlMs.ToString(CultureInfo.InvariantCulture),
            ]).ConfigureAwait(false);

        if ((int)result != 1)
        {
            return false;
        }

        lock (_held)
        {
            if (!_held.TryGetValue(resource, out var holders))
            {
                holders = [];
                _held[resource] = holders;
            }

            holders.Add(new Holder(holderId, DateTimeOffset.UtcNow));
            _refresher ??= Task.Run(() => RefreshLoopAsync(_stop.Token), CancellationToken.None);
        }

        return true;
    }

    /// <inheritdoc/>
    public async Task ReleaseAsync(string resource, CancellationToken ct = default)
    {
        string? holderId = null;
        lock (_held)
        {
            if (_held.TryGetValue(resource, out var holders) && holders.Count > 0)
            {
                holderId = holders[^1].Id;
                holders.RemoveAt(holders.Count - 1);
            }
        }

        // Nothing held by this node for the resource: another node's slot must never be released here.
        if (holderId is null)
        {
            return;
        }

        await _db.SortedSetRemoveAsync(HoldersKey(resource), holderId).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }

    private static string HoldersKey(string resource) => $"nexjob:throttle:holders:{resource}";

    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(_refreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await RefreshAllAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Store disposed: stop refreshing, held slots expire on their own.
        }
    }

    private async Task RefreshAllAsync()
    {
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(_capSeconds);
        var work = new List<(string Resource, RedisValue[] Args)>();
        lock (_held)
        {
            foreach (var (resource, holders) in _held)
            {
                // Past the maximum hold time a slot is no longer refreshed and expires by itself.
                var alive = holders.Where(h => h.AcquiredAt > cutoff).Select(h => (RedisValue)h.Id).ToList();
                if (alive.Count == 0)
                {
                    continue;
                }

                alive.Insert(0, _holderTtlMs.ToString(CultureInfo.InvariantCulture));
                work.Add((resource, alive.ToArray()));
            }
        }

        foreach (var (resource, args) in work)
        {
            try
            {
                await _db.ScriptEvaluateAsync(RefreshScript.ExecutableScript, [(RedisKey)HoldersKey(resource)], args).ConfigureAwait(false);
            }
            catch (RedisException)
            {
                // A missed refresh is retried on the next tick; the entry survives up to three intervals.
            }
        }
    }

    private sealed record Holder(string Id, DateTimeOffset AcquiredAt);
}
