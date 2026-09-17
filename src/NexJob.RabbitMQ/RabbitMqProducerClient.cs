using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using RabbitMQ.Client;

namespace NexJob.RabbitMQ;

/// <summary>
/// Default implementation of <see cref="IRabbitMqProducerClient"/> wrapping RabbitMQ.Client with publisher confirms.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class RabbitMqProducerClient : IRabbitMqProducerClient
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly RabbitMqProducerOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;
    private IModel? _channel;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RabbitMqProducerClient"/> class.
    /// </summary>
    /// <param name="connectionFactory">The RabbitMQ connection factory.</param>
    /// <param name="options">Configuration options for the producer.</param>
    public RabbitMqProducerClient(IConnectionFactory connectionFactory, RabbitMqProducerOptions options)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc/>
    public async Task PublishAsync(RabbitMqPublishPayload payload, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(payload);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureChannelOpen();

            var exchange = payload.Exchange ?? _options.DefaultExchange ?? string.Empty;
            var routingKey = payload.RoutingKey ?? _options.DefaultRoutingKey ?? string.Empty;
            var mandatory = payload.Mandatory ?? _options.Mandatory;

            var props = _channel!.CreateBasicProperties();
            props.Persistent = true;
            props.ContentType = payload.ContentType;

            if (!string.IsNullOrEmpty(payload.CorrelationId))
            {
                props.CorrelationId = payload.CorrelationId;
            }

            if (!string.IsNullOrEmpty(payload.MessageId))
            {
                props.MessageId = payload.MessageId;
            }

            props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            props.AppId = "NexJob";

            props.Headers = new Dictionary<string, object>(StringComparer.Ordinal);

            // W3C traceparent propagation
            var traceParent = Activity.Current?.Id;
            if (!string.IsNullOrWhiteSpace(traceParent))
            {
                props.Headers["traceparent"] = Encoding.UTF8.GetBytes(traceParent);
            }

            if (payload.Headers is not null)
            {
                foreach (var (headerKey, headerVal) in payload.Headers)
                {
                    if (!string.IsNullOrEmpty(headerKey) && headerVal is not null)
                    {
                        props.Headers[headerKey] = Encoding.UTF8.GetBytes(headerVal);
                    }
                }
            }

            byte[] bodyBytes = payload.ValueBytes ?? (payload.ValueString != null ? Encoding.UTF8.GetBytes(payload.ValueString) : Array.Empty<byte>());

            _channel.BasicPublish(
                exchange: exchange,
                routingKey: routingKey,
                mandatory: mandatory,
                basicProperties: props,
                body: bodyBytes);

            bool confirmed = _channel.WaitForConfirms(_options.ConfirmTimeout, out bool timedOut);
            if (timedOut)
            {
                ResetChannel();
                throw new TimeoutException($"RabbitMQ publisher confirm timed out after {_options.ConfirmTimeout.TotalSeconds}s for exchange '{exchange}' and routingKey '{routingKey}'.");
            }

            if (!confirmed)
            {
                throw new InvalidOperationException($"RabbitMQ publisher confirm received NACK from broker for exchange '{exchange}' and routingKey '{routingKey}'.");
            }
        }
        catch
        {
            ResetChannel();
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lock.Dispose();
        ResetChannel();

        try
        {
            _connection?.Dispose();
        }
        catch
        {
            // Suppress cleanup errors during shutdown
        }
        finally
        {
            _connection = null;
        }
    }

    private void EnsureChannelOpen()
    {
        if (_connection == null || !_connection.IsOpen)
        {
            ResetChannel();
            try
            {
                _connection?.Dispose();
            }
            catch
            {
                // Suppress disposal errors
            }

            if (_connectionFactory is ConnectionFactory concrete)
            {
                concrete.HostName = _options.HostName;
                concrete.Port = _options.Port;
                concrete.UserName = _options.UserName;
                concrete.Password = _options.Password;
                concrete.VirtualHost = _options.VirtualHost;
                concrete.AutomaticRecoveryEnabled = true;
            }

            _connection = _connectionFactory.CreateConnection();
        }

        if (_channel == null || !_channel.IsOpen)
        {
            ResetChannel();
            _channel = _connection.CreateModel();
            _channel.ConfirmSelect();
        }
    }

    private void ResetChannel()
    {
        try
        {
            _channel?.Dispose();
        }
        catch
        {
            // Suppress cleanup errors
        }
        finally
        {
            _channel = null;
        }
    }
}
