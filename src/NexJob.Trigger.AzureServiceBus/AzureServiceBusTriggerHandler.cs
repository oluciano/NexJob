using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexJob.Internal;
using NexJob.Storage;

namespace NexJob.Trigger.AzureServiceBus;

/// <summary>
/// Azure Service Bus trigger for NexJob. Receives messages from a Service Bus queue or topic
/// subscription and automatically enqueues them as NexJob jobs.
/// </summary>
internal sealed class AzureServiceBusTriggerHandler : IHostedService
{
    private readonly AzureServiceBusTriggerOptions _options;
    private readonly IScheduler _scheduler;
    private readonly NexJobOptions _nexJobOptions;
    private readonly ILogger<AzureServiceBusTriggerHandler> _logger;

    private ServiceBusClient? _client;
    private ServiceBusProcessor? _processor;

    /// <summary>
    /// Initializes a new <see cref="AzureServiceBusTriggerHandler"/>.
    /// </summary>
    /// <param name="options">Azure Service Bus trigger configuration.</param>
    /// <param name="scheduler">The NexJob scheduler for enqueueing jobs.</param>
    /// <param name="nexJobOptions">Global NexJob configuration options.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public AzureServiceBusTriggerHandler(
        IOptions<AzureServiceBusTriggerOptions> options,
        IScheduler scheduler,
        NexJobOptions nexJobOptions,
        ILogger<AzureServiceBusTriggerHandler> logger)
    {
        _options = options.Value;
        _scheduler = scheduler;
        _nexJobOptions = nexJobOptions;
        _logger = logger;
    }

    /// <summary>
    /// Starts the Azure Service Bus trigger.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _client = new ServiceBusClient(_options.ConnectionString);

        var processorOptions = new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = _options.MaxConcurrentMessages,
            AutoCompleteMessages = false,
        };

        _processor = _options.SubscriptionName is not null
            ? _client.CreateProcessor(_options.QueueOrTopicName, _options.SubscriptionName, processorOptions)
            : _client.CreateProcessor(_options.QueueOrTopicName, processorOptions);

        _processor.ProcessMessageAsync += HandleMessageAsync;
        _processor.ProcessErrorAsync += HandleErrorAsync;

        await _processor.StartProcessingAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Azure Service Bus trigger started. Queue/Topic: {QueueOrTopic}, Subscription: {Subscription}, Target queue: {TargetQueue}",
            _options.QueueOrTopicName,
            _options.SubscriptionName ?? "N/A",
            _options.TargetQueue);
    }

    /// <summary>
    /// Stops the Azure Service Bus trigger.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken).ConfigureAwait(false);
            await _processor.DisposeAsync().ConfigureAwait(false);
        }

        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }

        _logger.LogInformation("Azure Service Bus trigger stopped");
    }

    /// <summary>
    /// Extracts the W3C traceparent from message application properties.
    /// </summary>
    private static string? ExtractTraceparent(ServiceBusReceivedMessage message)
    {
        return message.ApplicationProperties.TryGetValue("traceparent", out var traceparent)
            ? traceparent?.ToString()
            : null;
    }

    /// <summary>
    /// Extracts the job type (assembly-qualified name) from message application properties.
    /// </summary>
    private static string ExtractJobType(ServiceBusReceivedMessage message)
    {
        if (message.ApplicationProperties.TryGetValue("nexjob.job_type", out var jobType) &&
            jobType is not null)
        {
            return jobType.ToString() ?? throw new InvalidOperationException("Job type is required in message properties");
        }

        throw new InvalidOperationException("Message must contain 'nexjob.job_type' in ApplicationProperties");
    }

    /// <summary>
    /// Processes a single message from Service Bus.
    /// </summary>
    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            // Extract message properties
            var messageId = args.Message.MessageId;
            var traceparent = ExtractTraceparent(args.Message);
            var jobType = ExtractJobType(args.Message);
            var inputJson = args.Message.Body.ToString();

            // Build JobRecord using factory
            var job = JobRecordFactory.Build(
                jobType: jobType,
                inputType: typeof(string).AssemblyQualifiedName!,
                inputJson: inputJson,
                options: _nexJobOptions,
                queue: _options.TargetQueue,
                priority: _options.JobPriority,
                idempotencyKey: messageId,
                status: JobStatus.Enqueued,
                scheduledAt: null,
                tags: new[] { "trigger:azuresb" },
                expiresAt: null,
                traceParent: traceparent);

            // Enqueue the job using scheduler — wake-up signal is handled internally
            await _scheduler.EnqueueAsync(job, DuplicatePolicy.AllowAfterFailed, args.CancellationToken).ConfigureAwait(false);

            // Complete message only after successful enqueue
            await args.CompleteMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Message {MessageId} enqueued as NexJob job. Job type: {JobType}",
                messageId,
                jobType);
        }
        catch (OperationCanceledException)
        {
            // Propagate cancellation — this is a shutdown signal
            throw;
        }
        catch (Exception ex)
        {
            // Enqueue failed — dead-letter the message
            _logger.LogWarning(
                ex,
                "Failed to enqueue message {MessageId}. Message will be dead-lettered.",
                args.Message.MessageId);

            try
            {
                await args.DeadLetterMessageAsync(
                    args.Message,
                    "EnqueueFailed",
                    ex.Message,
                    args.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception deadLetterEx)
            {
                _logger.LogError(
                    deadLetterEx,
                    "Failed to dead-letter message {MessageId}",
                    args.Message.MessageId);
            }
        }
    }

    /// <summary>
    /// Handles errors from the Service Bus processor.
    /// </summary>
    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(
            args.Exception,
            "Azure Service Bus processor error. Operation: {Operation}",
            args.ErrorSource);

        return Task.CompletedTask;
    }
}
