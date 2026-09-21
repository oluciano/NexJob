using System.ComponentModel.DataAnnotations;
using Confluent.Kafka;

namespace NexJob.Kafka;

/// <summary>
/// Configuration options for the NexJob resilient Kafka outbox producer.
/// </summary>
public sealed class KafkaProducerOptions
{
    /// <summary>
    /// Gets or sets the comma-separated list of Kafka broker endpoints.
    /// </summary>
    [Required(ErrorMessage = "BootstrapServers is required.")]
    public string BootstrapServers { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the acknowledgment level required from the broker.
    /// Defaults to <see cref="Acks.All"/>.
    /// </summary>
    public Acks Acks { get; set; } = Acks.All;

    /// <summary>
    /// Gets or sets a value indicating whether idempotence is enabled on the producer.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool EnableIdempotence { get; set; } = true;

    /// <summary>
    /// Gets or sets the timeout for flushing pending messages on host shutdown.
    /// Defaults to 10 seconds.
    /// </summary>
    public TimeSpan FlushTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the NexJob queue name used to process Kafka publishing jobs.
    /// Defaults to "kafka-producer".
    /// </summary>
    public string Queue { get; set; } = "kafka-producer";

    /// <summary>
    /// Gets or sets the default job priority for Kafka publishing jobs.
    /// Defaults to <see cref="JobPriority.Normal"/>.
    /// </summary>
    public JobPriority DefaultPriority { get; set; } = JobPriority.Normal;

    /// <summary>
    /// Gets or sets an optional delegate to configure or override the underlying Confluent.Kafka <see cref="ProducerConfig"/>.
    /// Used for SASL/SSL authentication, custom certificates (file location or PEM string), timeouts, and other advanced settings.
    /// </summary>
    public Action<ProducerConfig>? ConfigureProducer { get; set; }
}
