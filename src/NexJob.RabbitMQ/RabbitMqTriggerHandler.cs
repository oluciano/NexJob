using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexJob.Internal;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NexJob.Trigger.RabbitMQ;

/// <summary>
/// RabbitMQ trigger for NexJob. Receives messages from a RabbitMQ queue and automatically
/// enqueues them as NexJob jobs.
/// </summary>
internal sealed class RabbitMqTriggerHandler : IHostedService, IAsyncDisposable
{
    private readonly RabbitMqTriggerOptions _options;
    private readonly IConnectionFactory _connectionFactory;
    private readonly IScheduler _scheduler;
    private readonly NexJobOptions _nexJobOptions;
    private readonly ILogger<RabbitMqTriggerHandler> _logger;

    private IConnection? _connection;
    private IModel? _channel;
    private string? _consumerTag;
    private CancellationTokenSource? _stoppingCts;
    private Task? _reconnectTask;

    /// <summary>
    /// Initializes a new <see cref="RabbitMqTriggerHandler"/>.
    /// </summary>
    /// <param name="options">RabbitMQ trigger configuration.</param>
    /// <param name="connectionFactory">The RabbitMQ connection factory.</param>
    /// <param name="scheduler">The NexJob scheduler for enqueueing jobs.</param>
    /// <param name="nexJobOptions">Global NexJob configuration options.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public RabbitMqTriggerHandler(
        IOptions<RabbitMqTriggerOptions> options,
        IConnectionFactory connectionFactory,
        IScheduler scheduler,
        NexJobOptions nexJobOptions,
        ILogger<RabbitMqTriggerHandler> logger)
    {
        _options = options.Value;
        _connectionFactory = connectionFactory;
        _scheduler = scheduler;
        _nexJobOptions = nexJobOptions;
        _logger = logger;
    }

    /// <summary>
    /// Starts the RabbitMQ trigger.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingCts = new CancellationTokenSource();

        try
        {
            ConnectAndConsume();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Initial RabbitMQ connection failed. Starting reconnect loop.");
            _reconnectTask = ReconnectAsync(_stoppingCts.Token);
        }

        _logger.LogInformation(
            "RabbitMQ trigger started. Host: {Host}, Queue: {Queue}, Target queue: {TargetQueue}",
            _options.HostName,
            _options.QueueName,
            _options.TargetQueue);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Stops the RabbitMQ trigger gracefully.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stoppingCts is not null)
        {
            await _stoppingCts.CancelAsync().ConfigureAwait(false);
        }

        if (_reconnectTask is not null)
        {
            await Task.WhenAny(_reconnectTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
        }

        TeardownConnection();

        _stoppingCts?.Dispose();
        _stoppingCts = null;

        _logger.LogInformation("RabbitMQ trigger stopped");
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        TeardownConnection();
        if (_stoppingCts is not null)
        {
            await _stoppingCts.CancelAsync().ConfigureAwait(false);
            _stoppingCts.Dispose();
        }
    }

    private static string? ExtractTraceparent(IBasicProperties props)
    {
        if (props.Headers is not null &&
            props.Headers.TryGetValue("traceparent", out var value) &&
            value is byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        return null;
    }

    private static string ExtractJobType(IBasicProperties props)
    {
        if (props.Headers is not null &&
            props.Headers.TryGetValue("nexjob.job_type", out var value) &&
            value is byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        throw new InvalidOperationException("Message must contain 'nexjob.job_type' header.");
    }

    private void ConnectAndConsume()
    {
        if (_connectionFactory is ConnectionFactory concreteFactory)
        {
            concreteFactory.HostName = _options.HostName;
            concreteFactory.Port = _options.Port;
            concreteFactory.UserName = _options.UserName;
            concreteFactory.Password = _options.Password;
            concreteFactory.VirtualHost = _options.VirtualHost;
            concreteFactory.DispatchConsumersAsync = true;
        }

        _connection = _connectionFactory.CreateConnection();
        _channel = _connection.CreateModel();

        _channel.BasicQos(0, _options.PrefetchCount, false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += OnMessageReceivedAsync;

        _consumerTag = _channel.BasicConsume(
            queue: _options.QueueName,
            autoAck: false,
            consumer: consumer);

        _connection.ConnectionShutdown += (_, _) => OnConnectionDropped();
        _channel.CallbackException += (_, _) => OnConnectionDropped();

        _logger.LogInformation("Connected to RabbitMQ and started consuming from queue {Queue}", _options.QueueName);
    }

    private void OnConnectionDropped()
    {
        if (_stoppingCts is not null && !_stoppingCts.IsCancellationRequested && (_reconnectTask == null || _reconnectTask.IsCompleted))
        {
            _logger.LogWarning("RabbitMQ connection dropped. Starting reconnect loop.");
            _reconnectTask = ReconnectAsync(_stoppingCts.Token);
        }
    }

    private async Task ReconnectAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                TeardownConnection();
                ConnectAndConsume();
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RabbitMQ reconnect failed. Retrying in {Delay}...", _options.ReconnectDelay);
                await Task.Delay(_options.ReconnectDelay, ct).ConfigureAwait(false);
            }
        }
    }

    private void TeardownConnection()
    {
        try
        {
            if (_channel is not null)
            {
                if (_consumerTag is not null && _channel.IsOpen)
                {
                    _channel.BasicCancel(_consumerTag);
                }

                _channel.Dispose();
            }

            _connection?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error during RabbitMQ teardown");
        }
        finally
        {
            _channel = null;
            _connection = null;
            _consumerTag = null;
        }
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var deliveryTag = ea.DeliveryTag;

        try
        {
            // 1. Extract metadata
            var idempotencyKey = ea.BasicProperties.CorrelationId ?? ea.BasicProperties.MessageId;
            var traceparent = ExtractTraceparent(ea.BasicProperties);
            var jobType = ExtractJobType(ea.BasicProperties);
            var inputJson = Encoding.UTF8.GetString(ea.Body.ToArray());

            // 2. Build job record
            var job = JobRecordFactory.Build(
                jobType: jobType,
                inputType: typeof(string).AssemblyQualifiedName!,
                inputJson: inputJson,
                options: _nexJobOptions,
                queue: _options.TargetQueue,
                priority: _options.JobPriority,
                idempotencyKey: idempotencyKey,
                status: JobStatus.Enqueued,
                scheduledAt: null,
                tags: new[] { "trigger:rabbitmq" },
                expiresAt: null,
                traceParent: traceparent);

            // 3. Enqueue — wake-up signal is handled internally by IScheduler
            await _scheduler.EnqueueAsync(job, DuplicatePolicy.AllowAfterFailed, CancellationToken.None)
                .ConfigureAwait(false);

            // 4. Ack ONLY after successful enqueue
            _channel?.BasicAck(deliveryTag, multiple: false);

            _logger.LogInformation("RabbitMQ message {CorrelationId} enqueued as NexJob job.", idempotencyKey);
        }
        catch (OperationCanceledException)
        {
            // Shutdown in progress — nack with requeue so message is not lost
            _channel?.BasicNack(deliveryTag, multiple: false, requeue: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enqueue RabbitMQ message. Nacking with requeue: false.");
            // Permanent failure — nack without requeue (routes to DLX if configured)
            _channel?.BasicNack(deliveryTag, multiple: false, requeue: false);
        }
    }
}
