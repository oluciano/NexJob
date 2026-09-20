using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NexJob.Kafka;

namespace NexJob;

/// <summary>
/// Extension methods for registering and enqueuing messages with the NexJob resilient Kafka outbox producer.
/// </summary>
public static class KafkaProducerExtensions
{
    /// <summary>
    /// Registers the resilient Kafka outbox producer with the NexJob builder.
    /// </summary>
    /// <param name="builder">The NexJob builder.</param>
    /// <param name="configure">An action to configure the <see cref="KafkaProducerOptions"/>.</param>
    /// <returns>The NexJob builder for chaining.</returns>
    public static NexJobBuilder AddKafkaProducer(
        this NexJobBuilder builder,
        Action<KafkaProducerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddKafkaProducer(configure);
        return builder;
    }

    /// <summary>
    /// Registers the resilient Kafka outbox producer with the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure the <see cref="KafkaProducerOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddKafkaProducer(
        this IServiceCollection services,
        Action<KafkaProducerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<KafkaProducerOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddTransient<KafkaProducerJob>();

        services.AddSingleton<IKafkaProducerClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<KafkaProducerOptions>>().Value;
            var config = new ProducerConfig
            {
                BootstrapServers = options.BootstrapServers,
                Acks = options.Acks,
                EnableIdempotence = options.EnableIdempotence,
            };

            var producer = new ProducerBuilder<string, byte[]>(config).Build();
            return new ConfluentKafkaProducerClient(producer, options.FlushTimeout);
        });

        return services;
    }

    /// <summary>
    /// Registers the Kafka trigger (consumer) with the NexJob builder.
    /// </summary>
    /// <param name="builder">The NexJob builder.</param>
    /// <param name="configure">An action to configure the <see cref="Trigger.Kafka.KafkaTriggerOptions"/>.</param>
    /// <returns>The NexJob builder for chaining.</returns>
    public static NexJobBuilder AddKafkaTrigger(
        this NexJobBuilder builder,
        Action<Trigger.Kafka.KafkaTriggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        Trigger.Kafka.KafkaNexJobExtensions.AddNexJobKafkaTrigger(builder.Services, configure);
        return builder;
    }

    /// <summary>
    /// Registers the Kafka trigger (consumer) with the NexJob builder targeting a specific job.
    /// </summary>
    /// <typeparam name="TJob">The job type implementing <see cref="IJob{TInput}"/> with a string input.</typeparam>
    /// <param name="builder">The NexJob builder.</param>
    /// <param name="configure">An action to configure the <see cref="Trigger.Kafka.KafkaTriggerOptions"/>.</param>
    /// <returns>The NexJob builder for chaining.</returns>
    public static NexJobBuilder AddKafkaTrigger<TJob>(
        this NexJobBuilder builder,
        Action<Trigger.Kafka.KafkaTriggerOptions> configure)
        where TJob : class, IJob<string>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        Trigger.Kafka.KafkaNexJobExtensions.AddNexJobKafkaTrigger<TJob>(builder.Services, configure);
        return builder;
    }

    /// <summary>
    /// Registers the Kafka trigger (consumer) with the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure the <see cref="Trigger.Kafka.KafkaTriggerOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddKafkaTrigger(
        this IServiceCollection services,
        Action<Trigger.Kafka.KafkaTriggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        return Trigger.Kafka.KafkaNexJobExtensions.AddNexJobKafkaTrigger(services, configure);
    }

    /// <summary>
    /// Registers the Kafka trigger (consumer) with the service collection targeting a specific job.
    /// </summary>
    /// <typeparam name="TJob">The job type implementing <see cref="IJob{TInput}"/> with a string input.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure the <see cref="Trigger.Kafka.KafkaTriggerOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddKafkaTrigger<TJob>(
        this IServiceCollection services,
        Action<Trigger.Kafka.KafkaTriggerOptions> configure)
        where TJob : class, IJob<string>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        return Trigger.Kafka.KafkaNexJobExtensions.AddNexJobKafkaTrigger<TJob>(services, configure);
    }

    /// <summary>
    /// Enqueues a strongly typed object to be published durably to an Apache Kafka topic.
    /// The object is serialized to JSON using <see cref="System.Text.Json"/>.
    /// </summary>
    /// <typeparam name="T">The type of the message payload.</typeparam>
    /// <param name="scheduler">The NexJob scheduler instance.</param>
    /// <param name="topic">The target Kafka topic name.</param>
    /// <param name="key">The optional message partition key.</param>
    /// <param name="value">The object payload to serialize and publish.</param>
    /// <param name="headers">Optional custom headers to attach to the Kafka message.</param>
    /// <param name="jsonOptions">Optional JSON serializer options.</param>
    /// <param name="queue">The NexJob queue name. Defaults to "kafka-producer".</param>
    /// <param name="priority">The job priority. Defaults to <see cref="JobPriority.Normal"/>.</param>
    /// <param name="idempotencyKey">Optional deduplication key.</param>
    /// <param name="duplicatePolicy">Behavior when a duplicate key exists. Defaults to <see cref="DuplicatePolicy.AllowAfterFailed"/>.</param>
    /// <param name="tags">Optional searchable tags attached to the job.</param>
    /// <param name="deadlineAfter">Optional deadline for execution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identifier of the enqueued NexJob background job.</returns>
    public static Task<JobId> EnqueueKafkaAsync<T>(
        this IScheduler scheduler,
        string topic,
        string? key,
        T value,
        Dictionary<string, string>? headers = null,
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
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(value);

        KafkaPublishPayload payload;
        if (value is byte[] bytes)
        {
            payload = new KafkaPublishPayload
            {
                Topic = topic,
                Key = key,
                ValueBytes = bytes,
                Headers = headers,
            };
        }
        else if (value is string str)
        {
            payload = new KafkaPublishPayload
            {
                Topic = topic,
                Key = key,
                ValueString = str,
                Headers = headers,
            };
        }
        else
        {
            var json = JsonSerializer.Serialize(value, jsonOptions);
            payload = new KafkaPublishPayload
            {
                Topic = topic,
                Key = key,
                ValueString = json,
                Headers = headers,
            };
        }

        var jobTags = tags ?? new[] { "producer:kafka" };

        return scheduler.EnqueueAsync<KafkaProducerJob, KafkaPublishPayload>(
            payload,
            queue: queue ?? "kafka-producer",
            priority: priority,
            idempotencyKey: idempotencyKey,
            duplicatePolicy: duplicatePolicy,
            tags: jobTags,
            deadlineAfter: deadlineAfter,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Enqueues a raw string message to be published durably to an Apache Kafka topic.
    /// </summary>
    /// <param name="scheduler">The NexJob scheduler instance.</param>
    /// <param name="topic">The target Kafka topic name.</param>
    /// <param name="key">The optional message partition key.</param>
    /// <param name="value">The raw string payload to publish.</param>
    /// <param name="headers">Optional custom headers to attach to the Kafka message.</param>
    /// <param name="queue">The NexJob queue name. Defaults to "kafka-producer".</param>
    /// <param name="priority">The job priority. Defaults to <see cref="JobPriority.Normal"/>.</param>
    /// <param name="idempotencyKey">Optional deduplication key.</param>
    /// <param name="duplicatePolicy">Behavior when a duplicate key exists. Defaults to <see cref="DuplicatePolicy.AllowAfterFailed"/>.</param>
    /// <param name="tags">Optional searchable tags attached to the job.</param>
    /// <param name="deadlineAfter">Optional deadline for execution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identifier of the enqueued NexJob background job.</returns>
    public static Task<JobId> EnqueueKafkaRawAsync(
        this IScheduler scheduler,
        string topic,
        string? key,
        string value,
        Dictionary<string, string>? headers = null,
        string? queue = null,
        JobPriority priority = JobPriority.Normal,
        string? idempotencyKey = null,
        DuplicatePolicy duplicatePolicy = DuplicatePolicy.AllowAfterFailed,
        IReadOnlyList<string>? tags = null,
        TimeSpan? deadlineAfter = null,
        CancellationToken cancellationToken = default) =>
        scheduler.EnqueueKafkaAsync(
            topic,
            key,
            value,
            headers,
            queue: queue,
            priority: priority,
            idempotencyKey: idempotencyKey,
            duplicatePolicy: duplicatePolicy,
            tags: tags,
            deadlineAfter: deadlineAfter,
            cancellationToken: cancellationToken);

    /// <summary>
    /// Enqueues a raw binary byte array message to be published durably to an Apache Kafka topic.
    /// </summary>
    /// <param name="scheduler">The NexJob scheduler instance.</param>
    /// <param name="topic">The target Kafka topic name.</param>
    /// <param name="key">The optional message partition key.</param>
    /// <param name="value">The raw binary payload to publish.</param>
    /// <param name="headers">Optional custom headers to attach to the Kafka message.</param>
    /// <param name="queue">The NexJob queue name. Defaults to "kafka-producer".</param>
    /// <param name="priority">The job priority. Defaults to <see cref="JobPriority.Normal"/>.</param>
    /// <param name="idempotencyKey">Optional deduplication key.</param>
    /// <param name="duplicatePolicy">Behavior when a duplicate key exists. Defaults to <see cref="DuplicatePolicy.AllowAfterFailed"/>.</param>
    /// <param name="tags">Optional searchable tags attached to the job.</param>
    /// <param name="deadlineAfter">Optional deadline for execution.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The identifier of the enqueued NexJob background job.</returns>
    public static Task<JobId> EnqueueKafkaRawAsync(
        this IScheduler scheduler,
        string topic,
        string? key,
        byte[] value,
        Dictionary<string, string>? headers = null,
        string? queue = null,
        JobPriority priority = JobPriority.Normal,
        string? idempotencyKey = null,
        DuplicatePolicy duplicatePolicy = DuplicatePolicy.AllowAfterFailed,
        IReadOnlyList<string>? tags = null,
        TimeSpan? deadlineAfter = null,
        CancellationToken cancellationToken = default) =>
        scheduler.EnqueueKafkaAsync(
            topic,
            key,
            value,
            headers,
            queue: queue,
            priority: priority,
            idempotencyKey: idempotencyKey,
            duplicatePolicy: duplicatePolicy,
            tags: tags,
            deadlineAfter: deadlineAfter,
            cancellationToken: cancellationToken);
}
