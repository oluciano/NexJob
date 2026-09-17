using Microsoft.Extensions.Logging;

namespace NexJob.RabbitMQ;

/// <summary>
/// Background job that executes resilient publishing of a message to RabbitMQ.
/// </summary>
public sealed class RabbitMqProducerJob : IJob<RabbitMqPublishPayload>
{
    private readonly IRabbitMqProducerClient _producerClient;
    private readonly ILogger<RabbitMqProducerJob> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMqProducerJob"/> class.
    /// </summary>
    /// <param name="producerClient">The RabbitMQ producer client.</param>
    /// <param name="logger">Optional logger instance.</param>
    public RabbitMqProducerJob(
        IRabbitMqProducerClient producerClient,
        ILogger<RabbitMqProducerJob>? logger = null)
    {
        _producerClient = producerClient ?? throw new ArgumentNullException(nameof(producerClient));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RabbitMqProducerJob>.Instance;
    }

    /// <summary>
    /// Executes the RabbitMQ publish operation.
    /// </summary>
    /// <param name="input">The publish payload containing exchange, routing key, body, and headers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the publish operation.</returns>
    public async Task ExecuteAsync(RabbitMqPublishPayload input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        _logger.LogDebug(
            "Publishing message to RabbitMQ exchange '{Exchange}' with routing key '{RoutingKey}'.",
            input.Exchange,
            input.RoutingKey);

        await _producerClient.PublishAsync(input, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Successfully published message to RabbitMQ exchange '{Exchange}' with routing key '{RoutingKey}'.",
            input.Exchange,
            input.RoutingKey);
    }
}
