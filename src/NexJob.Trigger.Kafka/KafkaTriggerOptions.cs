using System.ComponentModel.DataAnnotations;

namespace NexJob.Trigger.Kafka;

/// <summary>
/// Configuration options for the NexJob Kafka trigger.
/// </summary>
public sealed class KafkaTriggerOptions
{
    /// <summary>
    /// Gets or sets the list of bootstrap servers for the Kafka cluster.
    /// </summary>
    [Required]
    public string BootstrapServers { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the topic to consume from.
    /// </summary>
    [Required]
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the group ID of the consumer.
    /// </summary>
    [Required]
    public string GroupId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target NexJob queue name. Defaults to "default".
    /// </summary>
    public string TargetQueue { get; set; } = "default";

    /// <summary>
    /// Gets or sets the priority of the enqueued NexJob jobs. Default: Normal.
    /// </summary>
    public JobPriority JobPriority { get; set; } = JobPriority.Normal;

    /// <summary>
    /// Gets or sets the name of the dead-letter topic. When set, failed messages are produced here
    /// before committing the offset. When null, offset is committed without DLT.
    /// </summary>
    public string? DeadLetterTopic { get; set; }

    /// <summary>
    /// Gets or sets the consume timeout for polling. Default: 1 second.
    /// </summary>
    public TimeSpan ConsumeTimeout { get; set; } = TimeSpan.FromSeconds(1);
}
