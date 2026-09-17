using Confluent.Kafka;

namespace NexJob.Trigger.Kafka;

/// <summary>
/// Abstraction over the Kafka consumer for testability.
/// </summary>
internal interface IKafkaConsumer : IDisposable
{
    /// <summary>
    /// Subscribes to the specified topic.
    /// </summary>
    /// <param name="topic">The topic to subscribe to.</param>
    void Subscribe(string topic);

    /// <summary>
    /// Consumes a single message from the subscribed topic.
    /// </summary>
    /// <param name="timeout">The timeout for polling.</param>
    /// <returns>The consume result, or null if no message was received within the timeout.</returns>
    ConsumeResult<string, string>? Consume(TimeSpan timeout);

    /// <summary>
    /// Commits the offset of the specified consume result.
    /// </summary>
    /// <param name="result">The consume result to commit.</param>
    void Commit(ConsumeResult<string, string> result);

    /// <summary>
    /// Closes the consumer, triggering final group rebalance.
    /// </summary>
    void Close();

    /// <summary>
    /// Produces a message to a dead-letter topic.
    /// </summary>
    /// <param name="topic">The DLT topic.</param>
    /// <param name="result">The original consume result.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ProduceToDeadLetterAsync(string topic, ConsumeResult<string, string> result, Exception exception, CancellationToken ct);
}
