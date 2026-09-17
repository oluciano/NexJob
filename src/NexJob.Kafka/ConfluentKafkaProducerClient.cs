using System.Diagnostics.CodeAnalysis;
using Confluent.Kafka;

namespace NexJob.Kafka;

/// <summary>
/// Default implementation of <see cref="IKafkaProducerClient"/> wrapping Confluent.Kafka's <see cref="IProducer{TKey, TValue}"/>.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class ConfluentKafkaProducerClient : IKafkaProducerClient
{
    private readonly IProducer<string, byte[]> _producer;
    private readonly TimeSpan _defaultFlushTimeout;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfluentKafkaProducerClient"/> class.
    /// </summary>
    /// <param name="producer">The underlying Confluent.Kafka producer instance.</param>
    /// <param name="defaultFlushTimeout">Default timeout used when flushing during disposal.</param>
    public ConfluentKafkaProducerClient(IProducer<string, byte[]> producer, TimeSpan defaultFlushTimeout)
    {
        _producer = producer ?? throw new ArgumentNullException(nameof(producer));
        _defaultFlushTimeout = defaultFlushTimeout;
    }

    /// <inheritdoc/>
    public Task<DeliveryResult<string, byte[]>> ProduceAsync(
        string topic,
        Message<string, byte[]> message,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _producer.ProduceAsync(topic, message, cancellationToken);
    }

    /// <inheritdoc/>
    public void Flush(TimeSpan timeout)
    {
        if (_disposed)
        {
            return;
        }

        _producer.Flush(timeout);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _producer.Flush(_defaultFlushTimeout);
        }
        catch
        {
            // Suppress flush errors during shutdown/disposal
        }
        finally
        {
            _producer.Dispose();
        }
    }
}
