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

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaTriggerHandler"/> class.
    /// </summary>
    /// <param name="options">Kafka trigger options.</param>
    /// <param name="consumer">The Kafka consumer.</param>
    /// <param name="scheduler">The NexJob scheduler.</param>
    /// <param name="nexJobOptions">The NexJob options.</param>
    /// <param name="logger">The logger.</param>
    public KafkaTriggerHandler(
        IOptions<KafkaTriggerOptions> options,
        IKafkaConsumer consumer,
        IScheduler scheduler,
        NexJobOptions nexJobOptions,
        ILogger<KafkaTriggerHandler> logger)
    {
        _options = options.Value;
        _consumer = consumer;
        _scheduler = scheduler;
        _nexJobOptions = nexJobOptions;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Kafka trigger started. Topic: {Topic}, Group: {Group}, Target queue: {TargetQueue}",
            _options.Topic,
            _options.GroupId,
            _options.TargetQueue);

        _consumer.Subscribe(_options.Topic);

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
                _logger.LogWarning(ex, "Kafka consume error on topic {Topic}", _options.Topic);
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in Kafka trigger polling loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
        }

        try
        {
            _consumer.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing Kafka consumer");
        }
    }

    private static string? ExtractTraceparent(Headers headers)
    {
        var header = headers.FirstOrDefault(h => string.Equals(h.Key, "traceparent", StringComparison.Ordinal));
        return header is not null ? Encoding.UTF8.GetString(header.GetValueBytes()) : null;
    }

    private static string ExtractJobType(Headers headers)
    {
        var header = headers.FirstOrDefault(h => string.Equals(h.Key, "nexjob.job_type", StringComparison.Ordinal));
        if (header is not null)
        {
            return Encoding.UTF8.GetString(header.GetValueBytes());
        }

        throw new InvalidOperationException("Message must contain 'nexjob.job_type' header.");
    }

    private async Task ProcessMessageAsync(
        ConsumeResult<string, string> result,
        CancellationToken ct)
    {
        var messageId = result.Message.Key ?? result.TopicPartitionOffset.ToString();
        var traceparent = ExtractTraceparent(result.Message.Headers);

        try
        {
            var jobType = ExtractJobType(result.Message.Headers);

            var job = JobRecordFactory.Build(
                jobType: jobType,
                inputType: typeof(string).AssemblyQualifiedName!,
                inputJson: result.Message.Value ?? string.Empty,
                options: _nexJobOptions,
                queue: _options.TargetQueue,
                priority: _options.JobPriority,
                idempotencyKey: messageId,
                status: JobStatus.Enqueued,
                scheduledAt: null,
                tags: new[] { "trigger:kafka" },
                expiresAt: null,
                traceParent: traceparent);

            // Enqueue — wake-up signal is handled internally by IScheduler
            await _scheduler.EnqueueAsync(job, DuplicatePolicy.AllowAfterFailed, ct)
                .ConfigureAwait(false);

            // Commit ONLY after successful enqueue
            _consumer.Commit(result);

            _logger.LogInformation("Kafka message {Key} enqueued as NexJob job.", messageId);
        }
        catch (OperationCanceledException)
        {
            // Shutdown — do not commit, message reprocessed on restart
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enqueue Kafka message {Key}.", messageId);

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
            }

            // If no DLT configured: do not commit — message reprocessed on restart
        }
    }
}
