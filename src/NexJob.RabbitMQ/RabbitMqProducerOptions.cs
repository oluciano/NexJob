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
    /// Gets or sets an optional delegate that configures the underlying <see cref="global::RabbitMQ.Client.ConnectionFactory"/>:
    /// TLS (<c>Ssl</c>), heartbeat, <c>ClientProvidedName</c> and any other property. It runs before
    /// <see cref="HostName"/>, <see cref="Port"/>, <see cref="UserName"/>, <see cref="Password"/> and <see cref="VirtualHost"/>
    /// are applied, so those five settings always win. A delegate that throws fails the connection attempt.
    /// </summary>
    public Action<global::RabbitMQ.Client.ConnectionFactory>? ConfigureConnection { get; set; }

    /// <summary>
    /// Gets or sets the exchange used when the call names none (a <see langword="null"/> exchange).
    /// Defaults to empty string (RabbitMQ default direct exchange).
    /// </summary>
    public string DefaultExchange { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the routing key used when the call passes a <see langword="null"/> routing key.
    /// </summary>
    public string? DefaultRoutingKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether messages should be published with the mandatory flag.
    /// A mandatory message the broker cannot route fails the publish with an <see cref="InvalidOperationException"/>.
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
