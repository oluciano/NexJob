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
    /// Gets or sets the target NexJob queue name. Defaults to "default".
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
}
