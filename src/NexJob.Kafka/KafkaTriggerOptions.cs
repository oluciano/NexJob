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
    /// Gets or sets the target NexJob queue name. Defaults to "default", which is the default queue of the application (<c>{prefix}.default</c>).
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
    /// Gets or sets the topic to which a job from this trigger is copied once it exhausts its retries. When set, the
    /// original message body is published there through the Kafka Outbox (<c>AddKafkaProducer</c> is required) and the
    /// job stays <c>Failed</c> in NexJob. Only jobs created by this trigger on <see cref="TargetQueue"/> are forwarded.
    /// Not to be confused with <see cref="DeadLetterTopic"/>, which receives messages that could never become a job.
    /// Defaults to <see langword="null"/> (no forwarding).
    /// </summary>
    public string? ExhaustedJobsTopic { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the forwarded message carries the last error in a
    /// <c>nexjob.error</c> header. Defaults to <see langword="false"/>.
    /// </summary>
    public bool ExhaustedJobsIncludeErrorHeader { get; set; }

    /// <summary>
    /// Gets or sets the consume timeout for polling. Default: 1 second.
    /// </summary>
    public TimeSpan ConsumeTimeout { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets or sets the target job type (assembly-qualified name or type name) to execute for consumed messages.
    /// When specified, messages do not require the 'nexjob.job_type' header.
    /// If both are present, the message header takes precedence.
    /// </summary>
    public string? JobType { get; set; }

    /// <summary>
    /// Gets or sets an optional delegate to configure or override the underlying Confluent.Kafka <see cref="Confluent.Kafka.ConsumerConfig"/>.
    /// Used for SASL/SSL authentication, custom certificates (file location or PEM string), timeouts, and other advanced settings.
    /// Note: <see cref="Confluent.Kafka.ConsumerConfig.EnableAutoCommit"/> is strictly enforced to <see langword="false"/> by NexJob and cannot be overridden.
    /// </summary>
    public Action<Confluent.Kafka.ConsumerConfig>? ConfigureConsumer { get; set; }
}
