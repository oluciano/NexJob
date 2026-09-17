namespace NexJob.RabbitMQ;

/// <summary>
/// Client abstraction for producing messages to RabbitMQ with publisher confirms.
/// </summary>
public interface IRabbitMqProducerClient : IDisposable
{
    /// <summary>
    /// Publishes a message to RabbitMQ and awaits broker publisher confirmation.
    /// </summary>
    /// <param name="payload">The publish payload containing exchange, routing key, body, and headers.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A task representing the asynchronous publish and confirmation operation.</returns>
    Task PublishAsync(RabbitMqPublishPayload payload, CancellationToken cancellationToken = default);
}
