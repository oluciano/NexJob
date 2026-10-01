namespace NexJob.Configuration;

/// <summary>Per-queue configuration entry in <c>appsettings.json</c>.</summary>
public sealed class QueueSettings
{
    /// <summary>Queue name. Required.</summary>
    public required string Name { get; set; }

    /// <summary>
    /// Not applied: the worker pool is global (<see cref="NexJobOptions.Workers"/>) and there is no per-queue pool.
    /// Setting it has no effect, and the dispatcher logs a warning at startup when it is set.
    /// </summary>
    public int? Workers { get; set; }

    /// <summary>Optional time window during which this queue is processed.</summary>
    public ExecutionWindowSettings? ExecutionWindow { get; set; }

    /// <summary>Optional circuit breaker options for this queue to protect downstream services.</summary>
    public QueueCircuitBreakerOptions? CircuitBreaker { get; set; }

    /// <summary>
    /// Configures and enables an automated circuit breaker on this queue.
    /// </summary>
    /// <param name="configure">Delegate to configure <see cref="QueueCircuitBreakerOptions"/>.</param>
    /// <returns>This instance for method chaining.</returns>
    public QueueSettings EnableCircuitBreaker(Action<QueueCircuitBreakerOptions> configure)
    {
        CircuitBreaker ??= new QueueCircuitBreakerOptions();
        configure(CircuitBreaker);
        return this;
    }
}
