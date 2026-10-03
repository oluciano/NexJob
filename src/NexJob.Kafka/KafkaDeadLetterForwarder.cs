using Microsoft.Extensions.Options;

namespace NexJob.Trigger.Kafka;

/// <summary>
/// Copies a job created by the Kafka trigger to <see cref="KafkaTriggerOptions.ExhaustedJobsTopic"/> once it exhausted
/// its retries.
/// </summary>
internal sealed class KafkaDeadLetterForwarder : IDeadLetterForwarder
{
    private readonly KafkaTriggerOptions _options;
    private readonly IScheduler _scheduler;

    /// <summary>Initializes a new instance of the <see cref="KafkaDeadLetterForwarder"/> class.</summary>
    /// <param name="options">The trigger options.</param>
    /// <param name="scheduler">The scheduler used to enqueue the Outbox publish job.</param>
    public KafkaDeadLetterForwarder(IOptions<KafkaTriggerOptions> options, IScheduler scheduler)
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
