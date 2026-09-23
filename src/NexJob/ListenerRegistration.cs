namespace NexJob;

/// <summary>
/// Immutable registration metadata for an event listener or trigger.
/// </summary>
/// <param name="Id">A unique identifier for this listener instance (e.g. 'rabbitmq:queue-name').</param>
/// <param name="Broker">The messaging broker or system name (e.g. 'RabbitMQ', 'Kafka', 'AzureServiceBus').</param>
/// <param name="Endpoint">The queue, topic, or subscription being consumed.</param>
/// <param name="TargetJobType">The name of the job type that messages are mapped to.</param>
/// <param name="JobTag">Optional tag attached to enqueued jobs for filtering (e.g. 'trigger:rabbitmq').</param>
/// <param name="ConsumerGroup">Optional consumer group or subscription identifier.</param>
public sealed record ListenerRegistration(
    string Id,
    string Broker,
    string Endpoint,
    string TargetJobType,
    string? JobTag = null,
    string? ConsumerGroup = null);
