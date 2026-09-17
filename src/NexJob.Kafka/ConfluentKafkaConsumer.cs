using System.Diagnostics.CodeAnalysis;
using System.Text;
using Confluent.Kafka;

namespace NexJob.Trigger.Kafka;

/// <summary>
/// Wrapper for Confluent.Kafka consumer implementing <see cref="IKafkaConsumer"/>.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class ConfluentKafkaConsumer : IKafkaConsumer
{
    private readonly IConsumer<string, string> _consumer;
    private readonly IProducer<string, string> _producer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfluentKafkaConsumer"/> class.
    /// </summary>
    /// <param name="consumer">The Confluent Kafka consumer.</param>
    /// <param name="bootstrapServers">The bootstrap servers for the producer.</param>
    public ConfluentKafkaConsumer(IConsumer<string, string> consumer, string bootstrapServers)
    {
        _consumer = consumer;
        var producerConfig = new ProducerConfig { BootstrapServers = bootstrapServers };
        _producer = new ProducerBuilder<string, string>(producerConfig).Build();
    }

    /// <inheritdoc/>
    public void Subscribe(string topic) => _consumer.Subscribe(topic);

    /// <inheritdoc/>
    public ConsumeResult<string, string>? Consume(TimeSpan timeout) => _consumer.Consume(timeout);

    /// <inheritdoc/>
    public void Commit(ConsumeResult<string, string> result) => _consumer.Commit(result);

    /// <inheritdoc/>
    public void Close() => _consumer.Close();

    /// <inheritdoc/>
    public async Task ProduceToDeadLetterAsync(string topic, ConsumeResult<string, string> result, Exception exception, CancellationToken ct)
    {
        var headers = new Headers
        {
            { "nexjob.error", Encoding.UTF8.GetBytes(exception.Message) },
            { "nexjob.original_topic", Encoding.UTF8.GetBytes(result.Topic) },
            { "nexjob.original_partition", BitConverter.GetBytes(result.Partition.Value) },
            { "nexjob.original_offset", BitConverter.GetBytes(result.Offset.Value) },
        };

        foreach (var header in result.Message.Headers)
        {
            headers.Add(header.Key, header.GetValueBytes());
        }

        var message = new Message<string, string>
        {
            Key = result.Message.Key,
            Value = result.Message.Value,
            Headers = headers,
        };

        await _producer.ProduceAsync(topic, message, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _consumer.Dispose();
        _producer.Dispose();
    }
}
