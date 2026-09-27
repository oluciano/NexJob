using NexJob.Configuration;

namespace NexJob.Internal;

/// <summary>
/// Snapshot of a queue's circuit breaker operational status.
/// </summary>
public sealed record QueueCircuitStatus(
    string Queue,
    QueueCircuitState State,
    int ConsecutiveFailures,
    int ConsecutiveProbeFailures,
    DateTimeOffset? OpenedAt,
    DateTimeOffset? NextProbeAt,
    TimeSpan? RemainingCooldown,
    int AllowedConcurrency);
