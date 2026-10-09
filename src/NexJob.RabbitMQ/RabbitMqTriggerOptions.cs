using System.ComponentModel.DataAnnotations;

namespace NexJob.Trigger.RabbitMQ;

/// <summary>
/// Configuration options for the NexJob RabbitMQ trigger.
/// </summary>
public sealed class RabbitMqTriggerOptions
{
    /// <summary>
    /// Gets or sets the host name of the RabbitMQ server. Default: "localhost".
    /// </summary>
    [Required]
    public string HostName { get; set; } = "localhost";

    /// <summary>
    /// Gets or sets the port number of the RabbitMQ server. Default: 5672.
    /// </summary>
    public int Port { get; set; } = 5672;

    /// <summary>
    /// Gets or sets the username for authenticating with RabbitMQ. Default: "guest".
    /// </summary>
    public string UserName { get; set; } = "guest";

    /// <summary>
    /// Gets or sets the password for authenticating with RabbitMQ. Default: "guest".
    /// </summary>
    public string Password { get; set; } = "guest";

    /// <summary>
    /// Gets or sets the virtual host to use in RabbitMQ. Default: "/".
    /// </summary>
    public string VirtualHost { get; set; } = "/";

    /// <summary>
    /// Gets or sets the name of the RabbitMQ queue to consume from.
    /// </summary>
    [Required]
    public string QueueName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of messages prefetched per consumer. Default: 1 (safest for at-least-once).
    /// </summary>
    public ushort PrefetchCount { get; set; } = 1;

    /// <summary>
    /// Gets or sets the target NexJob queue name. Defaults to "default", which is the default queue of the application (<c>{prefix}.default</c>).
    /// </summary>
    public string TargetQueue { get; set; } = "default";

    /// <summary>
    /// Gets or sets the priority of the enqueued NexJob jobs. Default: Normal.
    /// </summary>
    public JobPriority JobPriority { get; set; } = JobPriority.Normal;

    /// <summary>
    /// Gets or sets how long to wait before attempting reconnection after a connection failure. Default: 5 seconds.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the target job type (assembly-qualified name or type name) to execute for consumed messages.
    /// When specified, messages do not require the 'nexjob.job_type' header.
    /// If both are present, the message header takes precedence.
    /// </summary>
    public string? JobType { get; set; }

    /// <summary>
    /// Gets or sets the exchange to which a job from this trigger is copied once it exhausts its retries. An empty
    /// string (the default) is the default exchange, which routes by queue name.
    /// </summary>
    public string ExhaustedJobsExchange { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the routing key used when a job from this trigger is copied after exhausting its retries. When set,
    /// the original message body is published to <see cref="ExhaustedJobsExchange"/> through the RabbitMQ Outbox
    /// (<c>AddRabbitMqProducer</c> is required) and the job stays <c>Failed</c> in NexJob. With the default exchange the
    /// routing key is the name of the queue that receives the copy. Only jobs created by this trigger on
    /// <see cref="TargetQueue"/> are forwarded. Defaults to <see langword="null"/> (no forwarding).
    /// </summary>
    public string? ExhaustedJobsRoutingKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the forwarded message carries the last error in a
    /// <c>nexjob.error</c> header. Defaults to <see langword="false"/>.
    /// </summary>
    public bool ExhaustedJobsIncludeErrorHeader { get; set; }
}
