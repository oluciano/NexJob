using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;

namespace NexJob.Kafka;

/// <summary>
/// Background job that executes resilient publishing of a message to an Apache Kafka topic.
/// </summary>
public sealed class KafkaProducerJob : IJob<KafkaPublishPayload>
{
    private readonly IKafkaProducerClient _producerClient;
    private readonly ILogger<KafkaProducerJob> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaProducerJob"/> class.
    /// </summary>
    /// <param name="producerClient">The Kafka producer client.</param>
    /// <param name="logger">Optional logger instance. If null, a null logger is used.</param>
    public KafkaProducerJob(IKafkaProducerClient producerClient, ILogger<KafkaProducerJob>? logger = null)
    {
        _producerClient = producerClient ?? throw new ArgumentNullException(nameof(producerClient));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<KafkaProducerJob>.Instance;
    }

    /// <summary>
    /// Executes the Kafka publish operation.
    /// </summary>
    /// <param name="input">The publish payload containing topic, key, value, and headers.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A task that represents the asynchronous publish operation.</returns>
    public async Task ExecuteAsync(KafkaPublishPayload input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.Topic))
        {
            throw new ArgumentException("Kafka topic cannot be null or whitespace.", nameof(input));
        }

        byte[]? bodyBytes = input.ValueBytes;
        if (bodyBytes is null && input.ValueString is not null)
        {
            bodyBytes = Encoding.UTF8.GetBytes(input.ValueString);
        }

        var message = new Message<string, byte[]>
        {
            Key = input.Key ?? string.Empty,
            Value = bodyBytes ?? Array.Empty<byte>(),
            Headers = new Headers(),
        };

        // OpenTelemetry trace propagation
        var traceParent = Activity.Current?.Id;
        if (!string.IsNullOrWhiteSpace(traceParent))
        {
            message.Headers.Add("traceparent", Encoding.UTF8.GetBytes(traceParent));
        }

        // Custom headers
        if (input.Headers is not null)
        {
            foreach (var (headerKey, headerVal) in input.Headers)
            {
                if (!string.IsNullOrEmpty(headerKey) && headerVal is not null)
                {
                    message.Headers.Add(headerKey, Encoding.UTF8.GetBytes(headerVal));
                }
            }
        }

        _logger.LogDebug(
            "Publishing message to Kafka topic '{Topic}' with key '{Key}'.",
            input.Topic,
            input.Key);

        await _producerClient.ProduceAsync(input.Topic, message, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Successfully published message to Kafka topic '{Topic}' with key '{Key}'.",
            input.Topic,
            input.Key);
    }
}
