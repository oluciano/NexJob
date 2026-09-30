using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexJob.Internal;

namespace NexJob.Trigger.Kafka;

/// <summary>
/// Kafka trigger for NexJob. Polls a Kafka topic and enqueues messages as jobs.
/// </summary>
internal sealed class KafkaTriggerHandler : BackgroundService
{
    private readonly KafkaTriggerOptions _options;
    private readonly IKafkaConsumer _consumer;
    private readonly IScheduler _scheduler;
    private readonly NexJobOptions _nexJobOptions;
    private readonly ILogger<KafkaTriggerHandler> _logger;
    private readonly IListenerRegistry? _listenerRegistry;
    private readonly string _listenerId;

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaTriggerHandler"/> class.
    /// </summary>
    /// <param name="options">Kafka trigger options.</param>
    /// <param name="consumer">The Kafka consumer.</param>
    /// <param name="scheduler">The NexJob scheduler.</param>
    /// <param name="nexJobOptions">The NexJob options.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="listenerRegistry">Optional listener registry for observability.</param>
    public KafkaTriggerHandler(
        IOptions<KafkaTriggerOptions> options,
        IKafkaConsumer consumer,
        IScheduler scheduler,
        NexJobOptions nexJobOptions,
        ILogger<KafkaTriggerHandler> logger,
        IListenerRegistry? listenerRegistry = null)
    {
        _options = options.Value;
        _consumer = consumer;
        _scheduler = scheduler;
        _nexJobOptions = nexJobOptions;
        _logger = logger;
        _listenerRegistry = listenerRegistry;
        _listenerId = $"kafka:{_options.Topic}";

        _listenerRegistry?.Register(new ListenerRegistration(
            Id: _listenerId,
            Broker: "Kafka",
            Endpoint: _options.Topic,
            TargetJobType: _options.JobType ?? "Dynamic",
            JobTag: "trigger:kafka",
            ConsumerGroup: _options.GroupId));
    }

    /// <summary>Gets the delay before retrying a record whose enqueue failed transiently; the argument is the attempt number.</summary>
    internal Func<int, TimeSpan> TransientRetryDelay { get; init; } = DefaultTransientRetryDelay;

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Kafka trigger started. Topic: {Topic}, Group: {Group}, Target queue: {TargetQueue}",
            _options.Topic,
            _options.GroupId,
            _options.TargetQueue);

        _consumer.Subscribe(_options.Topic);
        _listenerRegistry?.UpdateStatus(_listenerId, ListenerStatus.Listening);

        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = null;
            try
            {
                result = _consumer.Consume(_options.ConsumeTimeout);

                if (result is null || result.IsPartitionEOF)
                {
                    continue;
                }

                await ProcessMessageAsync(result, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                _listenerRegistry?.UpdateStatus(_listenerId, ListenerStatus.Reconnecting, ex.Message);
                _logger.LogWarning(ex, "Kafka consume error on topic {Topic}", _options.Topic);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _listenerRegistry?.UpdateStatus(_listenerId, ListenerStatus.Faulted, ex.Message);
                _logger.LogError(ex, "Unexpected error in Kafka trigger polling loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
        }

        _listenerRegistry?.UpdateStatus(_listenerId, ListenerStatus.Stopped);

        try
        {
            _consumer.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing Kafka consumer");
        }
    }

    // Transient enqueue failures (storage down, network, timeouts) retry the same record in place with this
    // backoff, capped at the last value. The next record is not consumed meanwhile, so order is kept and no later
    // offset can be committed past the record that failed.
    private static TimeSpan DefaultTransientRetryDelay(int attempt)
    {
        int[] seconds = [1, 2, 5, 10, 20, 30];
        return TimeSpan.FromSeconds(seconds[Math.Min(attempt, seconds.Length - 1)]);
    }

    // The message itself is unusable and retrying can never help: missing job type, malformed payload/format.
    private static bool IsPermanent(Exception ex) =>
        ex is FormatException or System.Text.Json.JsonException or ArgumentException;

    private static string? ExtractTraceparent(Headers headers)
    {
        var header = headers.FirstOrDefault(h => string.Equals(h.Key, "traceparent", StringComparison.Ordinal));
        return header is not null ? Encoding.UTF8.GetString(header.GetValueBytes()) : null;
    }

    private string ExtractJobType(Headers headers)
    {
        var header = headers.FirstOrDefault(h => string.Equals(h.Key, "nexjob.job_type", StringComparison.Ordinal));
        if (header is not null)
        {
            return Encoding.UTF8.GetString(header.GetValueBytes());
        }

        if (!string.IsNullOrWhiteSpace(_options.JobType))
        {
            return _options.JobType;
        }

        throw new InvalidOperationException("Message must contain 'nexjob.job_type' header or JobType must be configured in KafkaTriggerOptions.");
    }

    private async Task ProcessMessageAsync(
        ConsumeResult<string, string> result,
        CancellationToken ct)
    {
        // The key is only for logs: two different records may share a key, so it must never drive deduplication.
        var messageId = result.Message.Key ?? result.TopicPartitionOffset.ToString();

        // A record is identified by its position. Redelivery of the same record yields the same key.
        var idempotencyKey = $"kafka:{result.Topic}:{result.Partition.Value}:{result.Offset.Value}";
        var traceparent = ExtractTraceparent(result.Message.Headers);

        JobRecord job;
        try
        {
            var jobType = ExtractJobType(result.Message.Headers);

            job = JobRecordFactory.Build(
                jobType: jobType,
                inputType: typeof(string).AssemblyQualifiedName!,
                inputJson: result.Message.Value ?? string.Empty,
                options: _nexJobOptions,
                queue: _options.TargetQueue,
                priority: _options.JobPriority,
                idempotencyKey: idempotencyKey,
                status: JobStatus.Enqueued,
                scheduledAt: null,
                tags: new[] { "trigger:kafka" },
                expiresAt: null,
                traceParent: traceparent);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Nothing that happens after this point can turn the message into a valid job.
            await HandlePermanentFailureAsync(result, messageId, ex, ct).ConfigureAwait(false);
            return;
        }

        var attempt = 0;
        while (true)
        {
            try
            {
                // Enqueue — wake-up signal is handled internally by IScheduler
                await _scheduler.EnqueueAsync(job, DuplicatePolicy.AllowAfterFailed, ct)
                    .ConfigureAwait(false);
                break;
            }
            catch (OperationCanceledException)
            {
                // Shutdown — do not commit, message reprocessed on restart
                throw;
            }
            catch (Exception ex) when (IsPermanent(ex))
            {
                await HandlePermanentFailureAsync(result, messageId, ex, ct).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                var delay = TransientRetryDelay(attempt);
                _logger.LogWarning(
                    ex,
                    "Failed to enqueue Kafka message {Key}; retrying the same record in {Delay}s (attempt {Attempt}).",
                    messageId,
                    delay.TotalSeconds,
                    attempt + 1);
                await Task.Delay(delay, ct).ConfigureAwait(false);
                attempt++;
            }
        }

        // Commit ONLY after successful enqueue. A failed commit is not an enqueue failure: the record is redelivered
        // and deduplicated by its position-based idempotency key.
        try
        {
            _consumer.Commit(result);
        }
        catch (Exception commitEx)
        {
            _logger.LogWarning(commitEx, "Kafka message {Key} was enqueued but its offset could not be committed.", messageId);
            return;
        }

        _logger.LogInformation("Kafka message {Key} enqueued as NexJob job.", messageId);
    }

    private async Task HandlePermanentFailureAsync(
        ConsumeResult<string, string> result,
        string messageId,
        Exception ex,
        CancellationToken ct)
    {
        _logger.LogWarning(ex, "Kafka message {Key} can never be enqueued.", messageId);

        if (_options.DeadLetterTopic is not null)
        {
            try
            {
                await _consumer.ProduceToDeadLetterAsync(_options.DeadLetterTopic, result, ex, ct).ConfigureAwait(false);

                // Commit after DLT production so we don't reprocess
                _consumer.Commit(result);
                _logger.LogInformation("Kafka message {Key} moved to dead-letter topic {DLT}.", messageId, _options.DeadLetterTopic);
            }
            catch (Exception dltEx)
            {
                _logger.LogError(dltEx, "Failed to move Kafka message {Key} to dead-letter topic {DLT}.", messageId, _options.DeadLetterTopic);
            }

            return;
        }

        // No DLT configured: skipping a poison message is better than blocking the partition forever.
        _logger.LogError(
            ex,
            "Kafka message {Key} skipped: it can never be enqueued and no dead-letter topic is configured.",
            messageId);
        try
        {
            _consumer.Commit(result);
        }
        catch (Exception commitEx)
        {
            _logger.LogWarning(commitEx, "Could not commit the offset of skipped Kafka message {Key}.", messageId);
        }
    }
}
