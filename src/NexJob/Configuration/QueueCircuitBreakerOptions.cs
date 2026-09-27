using System.Collections.Concurrent;

namespace NexJob.Configuration;

/// <summary>
/// Defines the operational states of a queue circuit breaker.
/// </summary>
public enum QueueCircuitState
{
    /// <summary>
    /// Circuit is closed and healthy. Jobs are dispatched normally up to full worker capacity.
    /// </summary>
    Closed,

    /// <summary>
    /// Circuit is tripped open due to consecutive downstream failures.
    /// The queue is paused, and workers skip this queue entirely.
    /// </summary>
    Open,

    /// <summary>
    /// Cooldown has elapsed. The queue allows exactly one canary job through to probe downstream health.
    /// </summary>
    HalfOpen,

    /// <summary>
    /// The canary job succeeded. The queue operates with restricted concurrency (ramp-up)
    /// to avoid overwhelming the recovering downstream service (anti-thundering herd).
    /// </summary>
    Recovering,
}

/// <summary>
/// Configuration options for an automated queue circuit breaker protecting downstream services.
/// </summary>
public sealed class QueueCircuitBreakerOptions
{
    private readonly HashSet<Type> _breakOnTypes = [];

    /// <summary>
    /// Gets or sets the number of consecutive eligible failures required to trip the circuit open.
    /// Defaults to <c>5</c>.
    /// </summary>
    public int ConsecutiveFailuresThreshold { get; set; } = 5;

    /// <summary>
    /// Gets or sets the initial cooldown period during which the queue remains paused.
    /// Defaults to <c>1 minute</c>.
    /// </summary>
    public TimeSpan OpenDuration { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the exponential backoff multiplier applied to <see cref="OpenDuration"/>
    /// when consecutive half-open/recovery probes fail.
    /// Defaults to <c>2.0</c>.
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Gets or sets the maximum cooldown cap for progressive backoff.
    /// Defaults to <c>15 minutes</c>.
    /// </summary>
    public TimeSpan MaxOpenDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets or sets the maximum concurrency (active worker slots) permitted during the <see cref="QueueCircuitState.Recovering"/> phase.
    /// Defaults to <c>2</c>.
    /// </summary>
    public int RecoveryConcurrency { get; set; } = 2;

    /// <summary>
    /// Gets or sets how long the queue remains in <see cref="QueueCircuitState.Recovering"/> before fully closing the circuit.
    /// Defaults to <c>2 minutes</c>.
    /// </summary>
    public TimeSpan RecoveryDuration { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Registers an exception type that is eligible to trip the circuit breaker.
    /// Subclasses of <typeparamref name="TException"/> are also considered eligible.
    /// If no exception types are explicitly registered, any unhandled exception trips the circuit.
    /// </summary>
    /// <typeparam name="TException">The exception type to monitor.</typeparam>
    /// <returns>This options instance for method chaining.</returns>
    public QueueCircuitBreakerOptions BreakOn<TException>()
        where TException : Exception
    {
        _breakOnTypes.Add(typeof(TException));
        return this;
    }

    /// <summary>
    /// Determines whether the specified exception matches the registered break criteria.
    /// </summary>
    /// <param name="ex">The exception to check.</param>
    /// <returns><see langword="true"/> if the exception should trigger circuit counting; otherwise <see langword="false"/>.</returns>
    public bool IsEligible(Exception ex)
    {
        if (_breakOnTypes.Count == 0)
        {
            return true;
        }

        var exType = ex.GetType();
        return _breakOnTypes.Any(registered => registered.IsAssignableFrom(exType));
    }
}
