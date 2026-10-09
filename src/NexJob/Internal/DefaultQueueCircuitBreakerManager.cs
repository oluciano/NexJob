using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NexJob.Configuration;

namespace NexJob.Internal;

/// <summary>
/// In-memory thread-safe implementation of <see cref="IQueueCircuitBreakerManager"/>.
/// </summary>
internal sealed class DefaultQueueCircuitBreakerManager : IQueueCircuitBreakerManager
{
    private readonly ConcurrentDictionary<string, CircuitEntry> _circuits = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<DefaultQueueCircuitBreakerManager>? _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultQueueCircuitBreakerManager"/> class.
    /// </summary>
    /// <param name="options">NexJob options containing queue settings.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="timeProvider">Optional time provider.</param>
    public DefaultQueueCircuitBreakerManager(NexJobOptions options, ILogger<DefaultQueueCircuitBreakerManager>? logger = null, TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        if (options.QueueSettings is { Count: > 0 })
        {
            foreach (var qs in options.QueueSettings.Where(s => s.CircuitBreaker is not null))
            {
                _circuits.TryAdd(options.ResolveQueue(qs.Name), new CircuitEntry(qs.CircuitBreaker!));
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultQueueCircuitBreakerManager"/> class with explicit configurations.
    /// </summary>
    /// <param name="configurations">Queue configurations mapping.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="timeProvider">Optional time provider.</param>
    internal DefaultQueueCircuitBreakerManager(IReadOnlyDictionary<string, QueueCircuitBreakerOptions> configurations, ILogger<DefaultQueueCircuitBreakerManager>? logger = null, TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;

        foreach (var kvp in configurations)
        {
            _circuits.TryAdd(kvp.Key, new CircuitEntry(kvp.Value));
        }
    }

    /// <inheritdoc/>
    public QueueCircuitState GetState(string queue, out int allowedConcurrency)
    {
        allowedConcurrency = int.MaxValue;
        if (!_circuits.TryGetValue(queue, out var entry))
        {
            return QueueCircuitState.Closed;
        }

        var now = _timeProvider.GetUtcNow();

        lock (entry.SyncRoot)
        {
            switch (entry.State)
            {
                case QueueCircuitState.Closed:
                    allowedConcurrency = int.MaxValue;
                    return QueueCircuitState.Closed;

                case QueueCircuitState.Open:
                    if (entry.NextProbeAt.HasValue && now >= entry.NextProbeAt.Value)
                    {
                        entry.State = QueueCircuitState.HalfOpen;
                        entry.ProbeInFlight = true;
                        allowedConcurrency = 1;
                        _logger?.LogInformation("Queue '{Queue}' circuit transitioned Open -> HalfOpen. Dispatching canary probe.", queue);
                        return QueueCircuitState.HalfOpen;
                    }

                    allowedConcurrency = 0;
                    return QueueCircuitState.Open;

                case QueueCircuitState.HalfOpen:
                    if (entry.ProbeInFlight)
                    {
                        allowedConcurrency = 0;
                    }
                    else
                    {
                        entry.ProbeInFlight = true;
                        allowedConcurrency = 1;
                    }

                    return QueueCircuitState.HalfOpen;

                case QueueCircuitState.Recovering:
                    if (entry.RecoveringStartedAt.HasValue && now - entry.RecoveringStartedAt.Value >= entry.Options.RecoveryDuration)
                    {
                        entry.State = QueueCircuitState.Closed;
                        entry.ConsecutiveFailures = 0;
                        entry.ConsecutiveProbeFailures = 0;
                        entry.OpenedAt = null;
                        entry.NextProbeAt = null;
                        entry.RecoveringStartedAt = null;
                        entry.ProbeInFlight = false;
                        allowedConcurrency = int.MaxValue;
                        _logger?.LogInformation("Queue '{Queue}' circuit successfully recovered. Transitioned Recovering -> Closed.", queue);
                        return QueueCircuitState.Closed;
                    }

                    allowedConcurrency = Math.Max(1, entry.Options.RecoveryConcurrency);
                    return QueueCircuitState.Recovering;

                default:
                    return QueueCircuitState.Closed;
            }
        }
    }

    /// <inheritdoc/>
    public void RecordOutcome(string queue, bool succeeded, Exception? exception)
    {
        if (!_circuits.TryGetValue(queue, out var entry))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        lock (entry.SyncRoot)
        {
            if (succeeded)
            {
                HandleSuccess(queue, entry, now);
            }
            else if (exception is not null && entry.Options.IsEligible(exception))
            {
                HandleFailure(queue, entry, now);
            }
            else
            {
                if (entry.State == QueueCircuitState.HalfOpen)
                {
                    entry.ProbeInFlight = false;
                }
            }
        }
    }

    /// <inheritdoc/>
    public void Reset(string queue)
    {
        if (_circuits.TryGetValue(queue, out var entry))
        {
            lock (entry.SyncRoot)
            {
                entry.State = QueueCircuitState.Closed;
                entry.ConsecutiveFailures = 0;
                entry.ConsecutiveProbeFailures = 0;
                entry.OpenedAt = null;
                entry.NextProbeAt = null;
                entry.RecoveringStartedAt = null;
                entry.ProbeInFlight = false;
                _logger?.LogInformation("Queue '{Queue}' circuit breaker manually reset to Closed.", queue);
            }
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<QueueCircuitStatus> GetAllStatuses()
    {
        var result = new List<QueueCircuitStatus>(_circuits.Count);
        foreach (var kvp in _circuits)
        {
            var status = GetStatus(kvp.Key);
            if (status is not null)
            {
                result.Add(status);
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public QueueCircuitStatus? GetStatus(string queue)
    {
        if (!_circuits.TryGetValue(queue, out var entry))
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        lock (entry.SyncRoot)
        {
            TimeSpan? remaining = null;
            if (entry.State == QueueCircuitState.Open && entry.NextProbeAt.HasValue)
            {
                var diff = entry.NextProbeAt.Value - now;
                remaining = diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
            }

            int allowed = entry.State switch
            {
                QueueCircuitState.Closed => int.MaxValue,
                QueueCircuitState.Open => 0,
                QueueCircuitState.HalfOpen => 1,
                QueueCircuitState.Recovering => entry.Options.RecoveryConcurrency,
                _ => int.MaxValue,
            };

            return new QueueCircuitStatus(
                queue,
                entry.State,
                entry.ConsecutiveFailures,
                entry.ConsecutiveProbeFailures,
                entry.OpenedAt,
                entry.NextProbeAt,
                remaining,
                allowed);
        }
    }

    private void HandleSuccess(string queue, CircuitEntry entry, DateTimeOffset now)
    {
        switch (entry.State)
        {
            case QueueCircuitState.Closed:
                entry.ConsecutiveFailures = 0;
                break;

            case QueueCircuitState.HalfOpen:
                entry.State = QueueCircuitState.Recovering;
                entry.RecoveringStartedAt = now;
                entry.ProbeInFlight = false;
                _logger?.LogInformation("Queue '{Queue}' canary probe succeeded. Transitioned HalfOpen -> Recovering (ramp-up concurrency: {Concurrency}).",
                    queue, entry.Options.RecoveryConcurrency);
                break;

            case QueueCircuitState.Recovering:
                if (entry.RecoveringStartedAt.HasValue && now - entry.RecoveringStartedAt.Value >= entry.Options.RecoveryDuration)
                {
                    entry.State = QueueCircuitState.Closed;
                    entry.ConsecutiveFailures = 0;
                    entry.ConsecutiveProbeFailures = 0;
                    entry.OpenedAt = null;
                    entry.NextProbeAt = null;
                    entry.RecoveringStartedAt = null;
                    entry.ProbeInFlight = false;
                    _logger?.LogInformation("Queue '{Queue}' circuit recovered during execution. Transitioned Recovering -> Closed.", queue);
                }

                break;

            case QueueCircuitState.Open:
                break;
        }
    }

    private void HandleFailure(string queue, CircuitEntry entry, DateTimeOffset now)
    {
        switch (entry.State)
        {
            case QueueCircuitState.Closed:
                entry.ConsecutiveFailures++;
                if (entry.ConsecutiveFailures >= entry.Options.ConsecutiveFailuresThreshold)
                {
                    TripOpen(queue, entry, now);
                }

                break;

            case QueueCircuitState.HalfOpen:
            case QueueCircuitState.Recovering:
                entry.ConsecutiveProbeFailures++;
                TripOpen(queue, entry, now, isRetrip: true);
                break;

            case QueueCircuitState.Open:
                break;
        }
    }

    private void TripOpen(string queue, CircuitEntry entry, DateTimeOffset now, bool isRetrip = false)
    {
        entry.State = QueueCircuitState.Open;
        entry.OpenedAt = now;
        entry.ProbeInFlight = false;
        entry.RecoveringStartedAt = null;

        var baseDuration = entry.Options.OpenDuration;
        var multiplier = isRetrip ? Math.Pow(entry.Options.BackoffMultiplier, entry.ConsecutiveProbeFailures) : 1.0;
        var computedSeconds = baseDuration.TotalSeconds * multiplier;
        var maxSeconds = entry.Options.MaxOpenDuration.TotalSeconds;
        var cooldown = TimeSpan.FromSeconds(Math.Min(computedSeconds, maxSeconds));

        entry.NextProbeAt = now + cooldown;

        _logger?.LogWarning(
            "Circuit breaker TRIPPED OPEN for queue '{Queue}'. Consecutive failures: {Failures} (probe failures: {ProbeFailures}). Pausing queue for {Cooldown}s until {NextProbeAt}.",
            queue,
            entry.ConsecutiveFailures,
            entry.ConsecutiveProbeFailures,
            cooldown.TotalSeconds,
            entry.NextProbeAt);
    }

    private sealed class CircuitEntry
    {
        public CircuitEntry(QueueCircuitBreakerOptions options)
        {
            Options = options;
        }

        public object SyncRoot { get; } = new();

        public QueueCircuitBreakerOptions Options { get; }

        public QueueCircuitState State { get; set; } = QueueCircuitState.Closed;

        public int ConsecutiveFailures { get; set; }

        public int ConsecutiveProbeFailures { get; set; }

        public DateTimeOffset? OpenedAt { get; set; }

        public DateTimeOffset? NextProbeAt { get; set; }

        public DateTimeOffset? RecoveringStartedAt { get; set; }

        public bool ProbeInFlight { get; set; }
    }
}
