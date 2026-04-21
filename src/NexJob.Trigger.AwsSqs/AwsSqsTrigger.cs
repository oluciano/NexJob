using Amazon.SQS.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexJob.Internal;
using NexJob.Storage;

namespace NexJob.Trigger.AwsSqs;

/// <summary>
/// AWS SQS trigger for NexJob. Receives messages from an SQS queue and automatically
/// enqueues them as NexJob jobs.
/// </summary>
internal sealed class AwsSqsTrigger : IHostedService
{
    private readonly AwsSqsTriggerOptions _options;
    private readonly ISqsClient _sqsClient;
    private readonly IScheduler _scheduler;
    private readonly NexJobOptions _nexJobOptions;
    private readonly ILogger<AwsSqsTrigger> _logger;

    private CancellationTokenSource? _stoppingCts;
    private Task? _pollingTask;

    /// <summary>
    /// Initializes a new <see cref="AwsSqsTrigger"/>.
    /// </summary>
    /// <param name="options">AWS SQS trigger configuration.</param>
    /// <param name="sqsClient">The SQS client for receiving messages.</param>
    /// <param name="scheduler">The NexJob scheduler for enqueueing jobs.</param>
    /// <param name="nexJobOptions">Global NexJob configuration options.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public AwsSqsTrigger(
        IOptions<AwsSqsTriggerOptions> options,
        ISqsClient sqsClient,
        IScheduler scheduler,
        NexJobOptions nexJobOptions,
        ILogger<AwsSqsTrigger> logger)
    {
        _options = options.Value;
        _sqsClient = sqsClient;
        _scheduler = scheduler;
        _nexJobOptions = nexJobOptions;
        _logger = logger;
    }

    /// <summary>
    /// Starts the AWS SQS trigger polling loop.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stoppingCts = new CancellationTokenSource();
        _pollingTask = Task.Run(() => PollLoopAsync(_stoppingCts.Token), cancellationToken);

        _logger.LogInformation(
            "AWS SQS trigger started. Queue URL: {QueueUrl}, Target queue: {TargetQueue}",
            _options.QueueUrl,
            _options.TargetQueue);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the AWS SQS trigger gracefully.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stoppingCts is not null)
        {
            await _stoppingCts.CancelAsync().ConfigureAwait(false);
        }

        if (_pollingTask is not null)
        {
            // Wait for the polling task to complete, respecting the shutdown token.
            await Task.WhenAny(_pollingTask, Task.Delay(Timeout.Infinite, cancellationToken))
                .ConfigureAwait(false);

            // Surface any fault from the polling task so it is not silently discarded.
            if (_pollingTask.IsFaulted)
            {
                try
                {
                    await _pollingTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AWS SQS polling task faulted.");
                }
            }
        }

        _stoppingCts?.Dispose();
        _stoppingCts = null;

        _logger.LogInformation("AWS SQS trigger stopped");
    }

    /// <summary>
    /// Extracts the W3C traceparent from message attributes.
    /// </summary>
    private static string? ExtractTraceparent(Message message)
    {
        if (message.MessageAttributes.TryGetValue("traceparent", out var attribute) &&
            attribute is not null &&
            !string.IsNullOrEmpty(attribute.StringValue))
        {
            return attribute.StringValue;
        }

        return null;
    }

    /// <summary>
    /// Main polling loop that continuously receives messages from SQS.
    /// </summary>
    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var receiveRequest = new ReceiveMessageRequest
                {
                    QueueUrl = _options.QueueUrl,
                    MaxNumberOfMessages = _options.MaxMessages,
                    WaitTimeSeconds = _options.WaitTimeSeconds,
                    VisibilityTimeout = _options.VisibilityTimeoutSeconds,
                    MessageAttributeNames = new List<string> { "All" },
                };

                var receiveResponse = await _sqsClient.ReceiveMessageAsync(
                    receiveRequest,
                    cancellationToken).ConfigureAwait(false);

                foreach (var message in receiveResponse.Messages)
                {
                    await ProcessMessageAsync(message, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error receiving messages from SQS queue {QueueUrl}", _options.QueueUrl);

                // Avoid tight loop on persistent errors
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Processes a single SQS message.
    /// </summary>
    private async Task ProcessMessageAsync(Message message, CancellationToken cancellationToken)
    {
        var receiptHandle = message.ReceiptHandle;
        var messageId = message.MessageId;

        _logger.LogInformation("Processing SQS message {MessageId}", messageId);

        // Start visibility extension loop
        using var extensionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var extensionTask = ExtendVisibilityAsync(receiptHandle, extensionCts.Token);

        try
        {
            // Extract trace context from message attributes
            var traceparent = ExtractTraceparent(message);

            // Build JobRecord using factory
            // <remarks>
            // The <c>inputType</c> is fixed to <see cref="string"/> because broker triggers
            // receive the message body as text (JSON, XML or plain text). Deserializing to a
            // concrete type is the responsibility of the job handler.
            // Support for custom <c>inputType</c> is planned for v2.2.
            // </remarks>
            var job = JobRecordFactory.Build(
                jobType: _options.JobName,
                inputType: typeof(string).AssemblyQualifiedName!,
                inputJson: message.Body ?? string.Empty,
                options: _nexJobOptions,
                queue: _options.TargetQueue,
                priority: _options.JobPriority,
                idempotencyKey: messageId,
                status: JobStatus.Enqueued,
                scheduledAt: null,
                tags: new[] { "trigger:awssqs" },
                expiresAt: null,
                traceParent: traceparent);

            // Enqueue the job using scheduler — wake-up signal is handled internally
            await _scheduler.EnqueueAsync(job, DuplicatePolicy.AllowAfterFailed, cancellationToken).ConfigureAwait(false);

            // Delete message only after successful enqueue
            var deleteRequest = new DeleteMessageRequest
            {
                QueueUrl = _options.QueueUrl,
                ReceiptHandle = receiptHandle,
            };

            await _sqsClient.DeleteMessageAsync(deleteRequest, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "SQS message {MessageId} enqueued as NexJob job and deleted. Job type: {JobType}",
                messageId,
                _options.JobName);
        }
        catch (OperationCanceledException)
        {
            // Propagate cancellation — this is a shutdown signal
            await extensionCts.CancelAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            // Enqueue failed — do NOT delete the message.
            // It will become visible again after visibility timeout expires,
            // and eventually go to DLQ after maxReceiveCount is exceeded.
            _logger.LogWarning(
                ex,
                "Failed to enqueue SQS message {MessageId}. Message will not be deleted and will reappear.",
                messageId);
        }
        finally
        {
            // Stop visibility extension loop
            await extensionCts.CancelAsync().ConfigureAwait(false);
            await extensionTask.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Periodically extends the visibility timeout of a message while it is being processed.
    /// </summary>
    private async Task ExtendVisibilityAsync(string receiptHandle, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_options.VisibilityExtensionIntervalSeconds),
                    cancellationToken).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var changeVisibilityRequest = new ChangeMessageVisibilityRequest
                {
                    QueueUrl = _options.QueueUrl,
                    ReceiptHandle = receiptHandle,
                    VisibilityTimeout = _options.VisibilityTimeoutSeconds,
                };

                await _sqsClient.ChangeMessageVisibilityAsync(changeVisibilityRequest, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogDebug(
                    "Extended visibility timeout for SQS message. Receipt: {ReceiptHandlePrefix}...",
                    receiptHandle[..Math.Min(8, receiptHandle.Length)]);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown or when processing completes
        }
        catch (Exception ex)
        {
            // Message may have been deleted or receipt handle expired — log and stop
            _logger.LogWarning(
                ex,
                "Failed to extend visibility timeout for SQS message. Receipt: {ReceiptHandlePrefix}...",
                receiptHandle[..Math.Min(8, receiptHandle.Length)]);
        }
    }
}
