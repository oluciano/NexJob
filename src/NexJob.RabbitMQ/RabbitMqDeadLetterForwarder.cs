using System.Text.Json;
using Microsoft.Extensions.Options;
using NexJob.RabbitMQ;

namespace NexJob.Trigger.RabbitMQ;

/// <summary>
/// Copies a job created by the RabbitMQ trigger to the configured exchange and routing key once it exhausted its
/// retries. The copy goes through the RabbitMQ Outbox, so it is durable and retried; the original job stays
/// <c>Failed</c> in NexJob.
/// </summary>
internal sealed class RabbitMqDeadLetterForwarder : IDeadLetterForwarder
{
    private const string ErrorHeader = "nexjob.error";
    private const string TriggerTag = "trigger:rabbitmq";

    private readonly RabbitMqTriggerOptions _options;
    private readonly IScheduler _scheduler;
    private readonly NexJobOptions _nexJobOptions;

    /// <summary>Initializes a new instance of the <see cref="RabbitMqDeadLetterForwarder"/> class.</summary>
    /// <param name="options">The trigger options.</param>
    /// <param name="scheduler">The scheduler used to enqueue the Outbox publish job.</param>
    /// <param name="nexJobOptions">The NexJob options, used to resolve the stored name of the target queue.</param>
    public RabbitMqDeadLetterForwarder(IOptions<RabbitMqTriggerOptions> options, IScheduler scheduler, NexJobOptions nexJobOptions)
    {
        _options = options.Value;
        _scheduler = scheduler;
        _nexJobOptions = nexJobOptions;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Only jobs this trigger created: on its target queue and tagged <c>trigger:rabbitmq</c>. The tag, not the
    /// idempotency key, identifies them because a RabbitMQ job only has a key when the publisher set a
    /// <c>MessageId</c>. That also keeps the Outbox publisher jobs and manually enqueued jobs out, so forwarding can
    /// never loop.
    /// </remarks>
    public bool AppliesTo(JobRecord failedJob)
    {
        if (string.IsNullOrWhiteSpace(_options.ExhaustedJobsRoutingKey))
        {
            return false;
        }

        return _nexJobOptions.StandsFor(_options.TargetQueue, failedJob.Queue)
            && failedJob.Tags.Contains(TriggerTag, StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public async Task ForwardAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
    {
        Dictionary<string, string>? headers = null;
        if (_options.ExhaustedJobsIncludeErrorHeader)
        {
            headers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ErrorHeader] = failedJob.LastErrorMessage ?? lastException.Message,
            };
        }

        await _scheduler.EnqueueRabbitMqRawAsync(
            _options.ExhaustedJobsExchange,
            _options.ExhaustedJobsRoutingKey!,
            RecoverBody(failedJob.InputJson),
            headers: headers,
            idempotencyKey: $"dead-letter-forward:{failedJob.Id.Value}",
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // The trigger stores the message body as a JSON string: recover it verbatim. Anything else is forwarded as stored.
    private static string RecoverBody(string inputJson)
    {
        try
        {
            return JsonSerializer.Deserialize<string>(inputJson) ?? string.Empty;
        }
        catch (JsonException)
        {
            return inputJson;
        }
    }
}
