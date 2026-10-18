using NexJob.Configuration;

namespace NexJob.Internal;

/// <summary>
/// Snapshot of a queue's circuit breaker operational status.
/// </summary>
/// <param name="Queue">Queue name.</param>
/// <param name="State">Current circuit state.</param>
/// <param name="ConsecutiveFailures">Consecutive failures counted while the circuit is closed.</param>
/// <param name="ConsecutiveProbeFailures">Consecutive failed probes while the circuit is half-open.</param>
/// <param name="OpenedAt">When the circuit last opened, or <see langword="null"/> if it never did.</param>
/// <param name="NextProbeAt">When the next probe is allowed, or <see langword="null"/> if none is scheduled.</param>
/// <param name="RemainingCooldown">Time left before the next probe, or <see langword="null"/> if none.</param>
/// <param name="AllowedConcurrency">Concurrency the queue is currently allowed.</param>
public sealed record QueueCircuitStatus(
    string Queue,
    QueueCircuitState State,
    int ConsecutiveFailures,
    int ConsecutiveProbeFailures,
    DateTimeOffset? OpenedAt,
    DateTimeOffset? NextProbeAt,
    TimeSpan? RemainingCooldown,
    int AllowedConcurrency);
