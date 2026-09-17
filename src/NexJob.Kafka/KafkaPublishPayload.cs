namespace NexJob.Kafka;

/// <summary>
/// Represents the payload for a Kafka message published via the NexJob outbox producer.
/// </summary>
public sealed class KafkaPublishPayload
{
    /// <summary>
    /// Gets or sets the target Kafka topic name.
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional message partition key.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// Gets or sets the optional raw binary message payload.
    /// </summary>
    public byte[]? ValueBytes { get; set; }

    /// <summary>
    /// Gets or sets the optional string message payload (e.g. JSON string).
    /// </summary>
    public string? ValueString { get; set; }

    /// <summary>
    /// Gets or sets custom headers to inject into the Kafka message.
    /// </summary>
    public Dictionary<string, string>? Headers { get; set; }
}
