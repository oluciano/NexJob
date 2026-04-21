using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexJob.Internal;

namespace NexJob.Trigger.GooglePubSub;

/// <summary>
/// Google Pub/Sub trigger for NexJob. Receives messages from a Pub/Sub subscription and automatically
/// enqueues them as NexJob jobs.
/// </summary>
internal sealed class GooglePubSubTriggerHandler : IHostedService
{
    private readonly GooglePubSubTriggerOptions _options;
    private readonly IPubSubSubscriber _subscriber;
    private readonly IScheduler _scheduler;
    private readonly NexJobOptions _nexJobOptions;
    private readonly ILogger<GooglePubSubTriggerHandler> _logger;
    private Task _runTask = Task.CompletedTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="GooglePubSubTriggerHandler"/> class.
    /// </summary>
    /// <param name="options">Google Pub/Sub trigger options.</param>
    /// <param name="subscriber">The Pub/Sub subscriber.</param>
    /// <param name="scheduler">The NexJob scheduler.</param>
    /// <param name="nexJobOptions">The NexJob options.</param>
    /// <param name="logger">The logger.</param>
    public GooglePubSubTriggerHandler(
        IOptions<GooglePubSubTriggerOptions> options,
        IPubSubSubscriber subscriber,
        IScheduler scheduler,
        NexJobOptions nexJobOptions,
        ILogger<GooglePubSubTriggerHandler> logger)
    {
        _options = options.Value;
        _subscriber = subscriber;
        _scheduler = scheduler;
        _nexJobOptions = nexJobOptions;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // StartAsync on the Google subscriber returns a Task that completes when the
        // subscriber is fully stopped — store it so StopAsync can observe it later.
        _runTask = _subscriber.StartAsync(HandleMessageAsync, cancellationToken);

        // Yield once so the task scheduler has a chance to execute any synchronous
        // startup work inside the subscriber before we inspect its state.
        await Task.Yield();

        // Surface an immediate fault (e.g. invalid credentials, missing subscription)
        // before the host considers startup successful.
        if (_runTask.IsFaulted)
        {
            await _runTask.ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Google Pub/Sub trigger started. Project: {Project}, Subscription: {Subscription}, Target queue: {TargetQueue}",
            _options.ProjectId,
            _options.SubscriptionId,
            _options.TargetQueue);
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _subscriber.StopAsync(cancellationToken).ConfigureAwait(false);

        // Await the run task so any startup failure surfaces here rather than
        // being silently discarded.
        try
        {
            await _runTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Google Pub/Sub subscriber run task faulted during shutdown.");
        }

        _logger.LogInformation("Google Pub/Sub trigger stopped.");
    }

    private static string ExtractJobType(PubsubMessage message)
    {
        if (message.Attributes.TryGetValue("nexjob.job_type", out var jobType) &&
            !string.IsNullOrEmpty(jobType))
        {
            return jobType;
        }

        throw new InvalidOperationException("Message must contain 'nexjob.job_type' attribute.");
    }

    private async Task<SubscriberClient.Reply> HandleMessageAsync(
        PubsubMessage message,
        CancellationToken ct)
    {
        try
        {
            var messageId = message.MessageId;
            var traceparent = message.Attributes.TryGetValue("traceparent", out var tp) ? tp : null;
            var jobType = ExtractJobType(message);
            var inputJson = message.Data.ToStringUtf8();

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
                tags: new[] { "trigger:googlepubsub" },
                expiresAt: null,
                traceParent: traceparent);

            // Enqueue — wake-up signal handled internally by IScheduler
            await _scheduler.EnqueueAsync(job, DuplicatePolicy.AllowAfterFailed, ct)
                .ConfigureAwait(false);

            _logger.LogInformation("Pub/Sub message {MessageId} enqueued as NexJob job.", messageId);
            return SubscriberClient.Reply.Ack;
        }
        catch (OperationCanceledException)
        {
            // Shutdown — nack so message is redelivered
            return SubscriberClient.Reply.Nack;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enqueue Pub/Sub message {MessageId}. Nacking.", message.MessageId);
            return SubscriberClient.Reply.Nack;
        }
    }
}
