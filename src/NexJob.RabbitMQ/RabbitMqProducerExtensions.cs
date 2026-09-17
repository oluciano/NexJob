using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NexJob.RabbitMQ;
using RabbitMQ.Client;

namespace NexJob;

/// <summary>
/// Extension methods for registering and enqueuing messages with the NexJob resilient RabbitMQ outbox producer.
/// </summary>
public static class RabbitMqProducerExtensions
{
    /// <summary>
    /// Registers the resilient RabbitMQ outbox producer with the NexJob builder.
    /// </summary>
    /// <param name="builder">The NexJob builder.</param>
    /// <param name="configure">An action to configure the <see cref="RabbitMqProducerOptions"/>.</param>
    /// <returns>The NexJob builder for chaining.</returns>
    public static NexJobBuilder AddRabbitMqProducer(
        this NexJobBuilder builder,
        Action<RabbitMqProducerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddRabbitMqProducer(configure);
        return builder;
    }

    /// <summary>
    /// Registers the resilient RabbitMQ outbox producer with the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure the <see cref="RabbitMqProducerOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRabbitMqProducer(
        this IServiceCollection services,
        Action<RabbitMqProducerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<RabbitMqProducerOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddTransient<RabbitMqProducerJob>();

        services.AddSingleton<IRabbitMqProducerClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RabbitMqProducerOptions>>().Value;
            var factory = sp.GetService<IConnectionFactory>() ?? new ConnectionFactory();
            return new RabbitMqProducerClient(factory, options);
        });

        return services;
    }

    /// <summary>
    /// Registers the RabbitMQ trigger (consumer) with the NexJob builder.
    /// </summary>
    /// <param name="builder">The NexJob builder.</param>
    /// <param name="configure">An action to configure the <see cref="Trigger.RabbitMQ.RabbitMqTriggerOptions"/>.</param>
    /// <returns>The NexJob builder for chaining.</returns>
    public static NexJobBuilder AddRabbitMqTrigger(
        this NexJobBuilder builder,
        Action<Trigger.RabbitMQ.RabbitMqTriggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddRabbitMqTrigger(configure);
        return builder;
    }

    /// <summary>
    /// Registers the RabbitMQ trigger (consumer) with the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure the <see cref="Trigger.RabbitMQ.RabbitMqTriggerOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRabbitMqTrigger(
        this IServiceCollection services,
        Action<Trigger.RabbitMQ.RabbitMqTriggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<Trigger.RabbitMQ.RabbitMqTriggerOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        if (services.All(s => s.ServiceType != typeof(IConnectionFactory)))
        {
            services.AddSingleton<IConnectionFactory>(_ => new ConnectionFactory());
        }

        services.AddHostedService<Trigger.RabbitMQ.RabbitMqTriggerHandler>();
        return services;
    }

    /// <summary>
    /// Enqueues a message to be published durably to RabbitMQ.
    /// The message value is serialized to JSON using System.Text.Json unless already a string or byte array.
    /// </summary>
    /// <typeparam name="T">The type of the message payload.</typeparam>
    /// <param name="scheduler">The NexJob scheduler instance.</param>
    /// <param name="exchange">The target RabbitMQ exchange name (use empty string for default direct exchange).</param>
    /// <param name="routingKey">The target routing key.</param>
    /// <param name="value">The message payload.</param>
    /// <param name="headers">Optional custom headers to attach to the RabbitMQ message.</param>
    /// <param name="mandatory">Optional mandatory flag for this message.</param>
    /// <param name="correlationId">Optional correlation ID.</param>
    /// <param name="messageId">Optional message ID.</param>
    /// <param name="jsonOptions">Optional JSON serialization options.</param>
    /// <param name="queue">The NexJob queue name. Defaults to "rabbitmq-producer".</param>
    /// <param name="priority">The job priority. Defaults to <see cref="JobPriority.Normal"/>.</param>
    /// <param name="idempotencyKey">Optional deduplication key.</param>
    /// <param name="duplicatePolicy">Behavior when a duplicate key exists. Defaults to <see cref="DuplicatePolicy.AllowAfterFailed"/>.</param>
    /// <param name="tags">Optional searchable tags attached to the job.</param>
    /// <param name="deadlineAfter">Optional deadline for execution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identifier of the enqueued NexJob background job.</returns>
    public static Task<JobId> EnqueueRabbitMqAsync<T>(
        this IScheduler scheduler,
        string exchange,
        string routingKey,
        T value,
        Dictionary<string, string>? headers = null,
        bool? mandatory = null,
        string? correlationId = null,
        string? messageId = null,
        JsonSerializerOptions? jsonOptions = null,
        string? queue = null,
        JobPriority priority = JobPriority.Normal,
        string? idempotencyKey = null,
        DuplicatePolicy duplicatePolicy = DuplicatePolicy.AllowAfterFailed,
        IReadOnlyList<string>? tags = null,
        TimeSpan? deadlineAfter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(value);

        RabbitMqPublishPayload payload;
        if (value is byte[] bytes)
        {
            payload = new RabbitMqPublishPayload
            {
                Exchange = exchange,
                RoutingKey = routingKey,
                ValueBytes = bytes,
                Headers = headers,
                Mandatory = mandatory,
                CorrelationId = correlationId,
                MessageId = messageId,
                ContentType = "application/octet-stream",
            };
        }
        else if (value is string str)
        {
            payload = new RabbitMqPublishPayload
            {
                Exchange = exchange,
                RoutingKey = routingKey,
                ValueString = str,
                Headers = headers,
                Mandatory = mandatory,
                CorrelationId = correlationId,
                MessageId = messageId,
                ContentType = "application/json",
            };
        }
        else
        {
            var json = JsonSerializer.Serialize(value, jsonOptions);
            payload = new RabbitMqPublishPayload
            {
                Exchange = exchange,
                RoutingKey = routingKey,
                ValueString = json,
                Headers = headers,
                Mandatory = mandatory,
                CorrelationId = correlationId,
                MessageId = messageId,
                ContentType = "application/json",
            };
        }

        var jobTags = tags ?? new[] { "producer:rabbitmq" };

        return scheduler.EnqueueAsync<RabbitMqProducerJob, RabbitMqPublishPayload>(
            payload,
            queue: queue ?? "rabbitmq-producer",
            priority: priority,
            idempotencyKey: idempotencyKey,
            duplicatePolicy: duplicatePolicy,
            tags: jobTags,
            deadlineAfter: deadlineAfter,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enqueues a message to be published to RabbitMQ using the producer's default exchange.
    /// </summary>
    /// <typeparam name="T">The type of the message payload.</typeparam>
    /// <param name="scheduler">The NexJob scheduler instance.</param>
    /// <param name="routingKey">The target routing key.</param>
    /// <param name="value">The message payload.</param>
    /// <param name="headers">Optional custom headers to attach to the RabbitMQ message.</param>
    /// <param name="queue">The NexJob queue name. Defaults to "rabbitmq-producer".</param>
    /// <param name="priority">The job priority. Defaults to <see cref="JobPriority.Normal"/>.</param>
    /// <param name="idempotencyKey">Optional deduplication key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identifier of the enqueued NexJob background job.</returns>
    public static Task<JobId> EnqueueRabbitMqAsync<T>(
        this IScheduler scheduler,
        string routingKey,
        T value,
        Dictionary<string, string>? headers = null,
        string? queue = null,
        JobPriority priority = JobPriority.Normal,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        return scheduler.EnqueueRabbitMqAsync(
            exchange: string.Empty,
            routingKey: routingKey,
            value: value,
            headers: headers,
            queue: queue,
            priority: priority,
            idempotencyKey: idempotencyKey,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enqueues a raw string message to be published durably to RabbitMQ.
    /// </summary>
    /// <param name="scheduler">The NexJob scheduler instance.</param>
    /// <param name="exchange">The target RabbitMQ exchange name.</param>
    /// <param name="routingKey">The target routing key.</param>
    /// <param name="value">The raw string payload to publish.</param>
    /// <param name="headers">Optional custom headers to attach to the RabbitMQ message.</param>
    /// <param name="queue">The NexJob queue name. Defaults to "rabbitmq-producer".</param>
    /// <param name="priority">The job priority. Defaults to <see cref="JobPriority.Normal"/>.</param>
    /// <param name="idempotencyKey">Optional deduplication key.</param>
    /// <param name="duplicatePolicy">Behavior when a duplicate key exists. Defaults to <see cref="DuplicatePolicy.AllowAfterFailed"/>.</param>
    /// <param name="tags">Optional searchable tags attached to the job.</param>
    /// <param name="deadlineAfter">Optional deadline for execution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identifier of the enqueued NexJob background job.</returns>
    public static Task<JobId> EnqueueRabbitMqRawAsync(
        this IScheduler scheduler,
        string exchange,
        string routingKey,
        string value,
        Dictionary<string, string>? headers = null,
        string? queue = null,
        JobPriority priority = JobPriority.Normal,
        string? idempotencyKey = null,
        DuplicatePolicy duplicatePolicy = DuplicatePolicy.AllowAfterFailed,
        IReadOnlyList<string>? tags = null,
        TimeSpan? deadlineAfter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(value);

        var payload = new RabbitMqPublishPayload
        {
            Exchange = exchange,
            RoutingKey = routingKey,
            ValueString = value,
            Headers = headers,
            ContentType = "application/json",
        };

        var jobTags = tags ?? new[] { "producer:rabbitmq" };

        return scheduler.EnqueueAsync<RabbitMqProducerJob, RabbitMqPublishPayload>(
            payload,
            queue: queue ?? "rabbitmq-producer",
            priority: priority,
            idempotencyKey: idempotencyKey,
            duplicatePolicy: duplicatePolicy,
            tags: jobTags,
            deadlineAfter: deadlineAfter,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enqueues a raw binary byte array message to be published durably to RabbitMQ.
    /// </summary>
    /// <param name="scheduler">The NexJob scheduler instance.</param>
    /// <param name="exchange">The target RabbitMQ exchange name.</param>
    /// <param name="routingKey">The target routing key.</param>
    /// <param name="value">The raw byte array payload to publish.</param>
    /// <param name="headers">Optional custom headers to attach to the RabbitMQ message.</param>
    /// <param name="queue">The NexJob queue name. Defaults to "rabbitmq-producer".</param>
    /// <param name="priority">The job priority. Defaults to <see cref="JobPriority.Normal"/>.</param>
    /// <param name="idempotencyKey">Optional deduplication key.</param>
    /// <param name="duplicatePolicy">Behavior when a duplicate key exists. Defaults to <see cref="DuplicatePolicy.AllowAfterFailed"/>.</param>
    /// <param name="tags">Optional searchable tags attached to the job.</param>
    /// <param name="deadlineAfter">Optional deadline for execution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identifier of the enqueued NexJob background job.</returns>
    public static Task<JobId> EnqueueRabbitMqRawAsync(
        this IScheduler scheduler,
        string exchange,
        string routingKey,
        byte[] value,
        Dictionary<string, string>? headers = null,
        string? queue = null,
        JobPriority priority = JobPriority.Normal,
        string? idempotencyKey = null,
        DuplicatePolicy duplicatePolicy = DuplicatePolicy.AllowAfterFailed,
        IReadOnlyList<string>? tags = null,
        TimeSpan? deadlineAfter = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(value);

        var payload = new RabbitMqPublishPayload
        {
            Exchange = exchange,
            RoutingKey = routingKey,
            ValueBytes = value,
            Headers = headers,
            ContentType = "application/octet-stream",
        };

        var jobTags = tags ?? new[] { "producer:rabbitmq" };

        return scheduler.EnqueueAsync<RabbitMqProducerJob, RabbitMqPublishPayload>(
            payload,
            queue: queue ?? "rabbitmq-producer",
            priority: priority,
            idempotencyKey: idempotencyKey,
            duplicatePolicy: duplicatePolicy,
            tags: jobTags,
            deadlineAfter: deadlineAfter,
            cancellationToken: cancellationToken);
    }
}
