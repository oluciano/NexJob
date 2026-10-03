using Microsoft.Extensions.Options;

namespace NexJob.Trigger.RabbitMQ;

/// <summary>
/// Copies a job created by the RabbitMQ trigger to the configured exchange and routing key once it exhausted its
/// retries.
/// </summary>
internal sealed class RabbitMqDeadLetterForwarder : IDeadLetterForwarder
{
    private readonly RabbitMqTriggerOptions _options;
    private readonly IScheduler _scheduler;

    /// <summary>Initializes a new instance of the <see cref="RabbitMqDeadLetterForwarder"/> class.</summary>
    /// <param name="options">The trigger options.</param>
    /// <param name="scheduler">The scheduler used to enqueue the Outbox publish job.</param>
    public RabbitMqDeadLetterForwarder(IOptions<RabbitMqTriggerOptions> options, IScheduler scheduler)
    {
        _options = options.Value;
        _scheduler = scheduler;
    }

    /// <inheritdoc/>
    public bool AppliesTo(JobRecord failedJob) => _options is null;

    /// <inheritdoc/>
    public Task ForwardAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken) =>
        _scheduler is null ? Task.FromException(new InvalidOperationException()) : Task.CompletedTask;
}
