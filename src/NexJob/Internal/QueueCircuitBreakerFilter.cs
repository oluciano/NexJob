using NexJob.Internal;

namespace NexJob;

/// <summary>
/// Built-in job execution filter that records job success and failure outcomes into
/// <see cref="IQueueCircuitBreakerManager"/> to drive automated queue circuit breaking.
/// </summary>
internal sealed class QueueCircuitBreakerFilter : IJobExecutionFilter
{
    private readonly IQueueCircuitBreakerManager _circuitManager;

    /// <summary>
    /// Initializes a new instance of <see cref="QueueCircuitBreakerFilter"/>.
    /// </summary>
    /// <param name="circuitManager">The queue circuit breaker manager.</param>
    public QueueCircuitBreakerFilter(IQueueCircuitBreakerManager circuitManager)
    {
        _circuitManager = circuitManager;
    }

    /// <inheritdoc/>
    public async Task OnExecutingAsync(JobExecutingContext context, JobExecutionDelegate next, CancellationToken ct)
    {
        try
        {
            await next(ct).ConfigureAwait(false);
            _circuitManager.RecordOutcome(context.Job.Queue, succeeded: true, exception: null);
        }
        catch (Exception ex)
        {
            _circuitManager.RecordOutcome(context.Job.Queue, succeeded: false, exception: ex);
            throw;
        }
    }
}
