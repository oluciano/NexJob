using System.ComponentModel.DataAnnotations;

namespace NexJob.RabbitMQ;

/// <summary>
/// Configuration options for the NexJob resilient RabbitMQ outbox producer.
/// </summary>
public sealed class RabbitMqProducerOptions
{
    /// <summary>
    /// Gets or sets the host name of the RabbitMQ server. Default: "localhost".
    /// </summary>
    [Required(ErrorMessage = "HostName is required.")]
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
    /// Gets or sets the default exchange name to publish to.
    /// Defaults to empty string (RabbitMQ default direct exchange).
    /// </summary>
    public string DefaultExchange { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the default routing key to use if none is specified per message.
    /// </summary>
    public string? DefaultRoutingKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether messages should be published with the mandatory flag.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    public bool Mandatory { get; set; }

    /// <summary>
    /// Gets or sets the timeout for waiting for broker publisher confirms.
    /// Defaults to 5 seconds.
    /// </summary>
    public TimeSpan ConfirmTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the NexJob queue name for RabbitMQ publishing jobs.
    /// Not read yet: the queue comes from the <c>queue</c> argument of <c>EnqueueRabbitMqAsync</c>, which defaults to "default", the default queue of the application (<c>{prefix}.default</c>).
    /// Defaults to "default", the default queue of the application (<c>{prefix}.default</c>).
    /// </summary>
    public string Queue { get; set; } = "default";

    /// <summary>
    /// Gets or sets the default job priority for RabbitMQ publishing jobs.
    /// Defaults to <see cref="JobPriority.Normal"/>.
    /// </summary>
    public JobPriority DefaultPriority { get; set; } = JobPriority.Normal;
}
