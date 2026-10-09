using System.Text.Json;
using Microsoft.Extensions.Options;
using NexJob.Kafka;

namespace NexJob.Trigger.Kafka;

/// <summary>
/// Copies a job created by the Kafka trigger to <see cref="KafkaTriggerOptions.ExhaustedJobsTopic"/> once it exhausted
/// its retries. The copy goes through the Kafka Outbox, so it is durable and retried; the original job stays
/// <c>Failed</c> in NexJob.
/// </summary>
internal sealed class KafkaDeadLetterForwarder : IDeadLetterForwarder
{
    private const string ErrorHeader = "nexjob.error";

    private readonly KafkaTriggerOptions _options;
    private readonly IScheduler _scheduler;
    private readonly NexJobOptions _nexJobOptions;

    /// <summary>Initializes a new instance of the <see cref="KafkaDeadLetterForwarder"/> class.</summary>
    /// <param name="options">The trigger options.</param>
    /// <param name="scheduler">The scheduler used to enqueue the Outbox publish job.</param>
    /// <param name="nexJobOptions">The NexJob options, used to resolve the stored name of the target queue.</param>
    public KafkaDeadLetterForwarder(IOptions<KafkaTriggerOptions> options, IScheduler scheduler, NexJobOptions nexJobOptions)
    {
        _options = options.Value;
        _scheduler = scheduler;
        _nexJobOptions = nexJobOptions;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Only jobs this trigger created: on its target queue and carrying the idempotency key the trigger gives every
    /// message (<c>kafka:{topic}:{partition}:{offset}</c>). That also keeps the Outbox publisher jobs and manually
    /// enqueued jobs out, so forwarding can never loop.
    /// </remarks>
    public bool AppliesTo(JobRecord failedJob)
    {
        _ = _nexJobOptions;
        if (string.IsNullOrWhiteSpace(_options.ExhaustedJobsTopic))
        {
            return false;
        }

        return string.Equals(failedJob.Queue, _options.TargetQueue, StringComparison.Ordinal)
            && failedJob.IdempotencyKey is { } key
            && key.StartsWith($"kafka:{_options.Topic}:", StringComparison.Ordinal);
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

        await _scheduler.EnqueueKafkaRawAsync(
            _options.ExhaustedJobsTopic!,
            key: null,
            value: RecoverBody(failedJob.InputJson),
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
