using Confluent.Kafka;

namespace NexJob.Kafka;

/// <summary>
/// Client abstraction for producing messages to Apache Kafka.
/// </summary>
public interface IKafkaProducerClient : IDisposable
{
    /// <summary>
    /// Produces a message to the specified topic asynchronously.
    /// </summary>
    /// <param name="topic">The target Kafka topic name.</param>
    /// <param name="message">The message to produce.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A task representing the asynchronous produce operation with delivery result.</returns>
    Task<DeliveryResult<string, byte[]>> ProduceAsync(
        string topic,
        Message<string, byte[]> message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes any pending message batches to the Kafka broker synchronously.
    /// </summary>
    /// <param name="timeout">Maximum duration to wait for flush to complete.</param>
    void Flush(TimeSpan timeout);
}
