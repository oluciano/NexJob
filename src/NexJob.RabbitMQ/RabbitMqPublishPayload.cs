namespace NexJob.RabbitMQ;

/// <summary>
/// Represents the payload for a RabbitMQ message published via the NexJob outbox producer.
/// </summary>
public sealed class RabbitMqPublishPayload
{
    /// <summary>
    /// Gets or sets the target RabbitMQ exchange name.
    /// If null, the producer's default exchange is used.
    /// </summary>
    public string? Exchange { get; set; }

    /// <summary>
    /// Gets or sets the target routing key.
    /// </summary>
    public string RoutingKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional raw binary message payload.
    /// </summary>
    public byte[]? ValueBytes { get; set; }

    /// <summary>
    /// Gets or sets the optional string message payload (e.g. serialized JSON).
    /// </summary>
    public string? ValueString { get; set; }

    /// <summary>
    /// Gets or sets custom headers to inject into the RabbitMQ message.
    /// </summary>
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Gets or sets the optional mandatory flag per message. If null, the producer default is used.
    /// </summary>
    public bool? Mandatory { get; set; }

    /// <summary>
    /// Gets or sets the content type of the message payload. Defaults to "application/json".
    /// </summary>
    public string ContentType { get; set; } = "application/json";

    /// <summary>
    /// Gets or sets the optional correlation ID for request-reply or distributed tracking.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the optional unique message identifier.
    /// </summary>
    public string? MessageId { get; set; }
}
