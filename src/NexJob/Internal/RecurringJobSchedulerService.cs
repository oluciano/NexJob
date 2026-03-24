using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Storage;

namespace NexJob.Internal;

/// <summary>
/// Hosted background service that polls the storage provider for due recurring jobs,
/// enqueues them as normal <see cref="JobRecord"/> instances, and advances their
/// <see cref="RecurringJobRecord.NextExecution"/> timestamp.
/// </summary>
internal sealed class RecurringJobSchedulerService : BackgroundService
{
    private readonly IStorageProvider _storage;
    private readonly NexJobOptions _options;
    private readonly ILogger<RecurringJobSchedulerService> _logger;

    /// <summary>
    /// Initializes a new <see cref="RecurringJobSchedulerService"/>.
    /// </summary>
    public RecurringJobSchedulerService(
        IStorageProvider storage,
        NexJobOptions options,
        ILogger<RecurringJobSchedulerService> logger)
    {
        _storage = storage;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RecurringJobSchedulerService started. Polling every {Interval}.",
            _options.PollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EnqueueDueJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RecurringJobSchedulerService");
            }

            await Task.Delay(_options.PollingInterval, stoppingToken);
        }

        _logger.LogInformation("RecurringJobSchedulerService stopped.");
    }

    // ─── private ─────────────────────────────────────────────────────────────

    private async Task EnqueueDueJobsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var dueJobs = await _storage.GetDueRecurringJobsAsync(now, cancellationToken);

        foreach (var recurring in dueJobs)
        {
            try
            {
                var jobRecord = new JobRecord
                {
                    Id = JobId.New(),
                    JobType = recurring.JobType,
                    InputType = recurring.InputType,
                    InputJson = recurring.InputJson,
                    Queue = recurring.Queue,
                    Priority = JobPriority.Normal,
                    Status = JobStatus.Enqueued,
                    CreatedAt = DateTimeOffset.UtcNow,
                    MaxAttempts = _options.MaxAttempts,
                };

                await _storage.EnqueueAsync(jobRecord, cancellationToken);

                var nextExecution = CalculateNextExecution(recurring);

                if (nextExecution.HasValue)
                {
                    await _storage.SetRecurringJobNextExecutionAsync(
                        recurring.RecurringJobId, nextExecution.Value, cancellationToken);
                }

                _logger.LogDebug(
                    "Enqueued recurring job '{Id}', next execution: {Next}",
                    recurring.RecurringJobId, nextExecution);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to enqueue recurring job '{Id}'", recurring.RecurringJobId);
            }
        }
    }

    private static DateTimeOffset? CalculateNextExecution(RecurringJobRecord recurring)
    {
        var tz = recurring.TimeZoneId is not null
            ? TimeZoneInfo.FindSystemTimeZoneById(recurring.TimeZoneId)
            : TimeZoneInfo.Utc;

        var cronExpression = DefaultScheduler.ParseCron(recurring.Cron);
        return cronExpression.GetNextOccurrence(DateTimeOffset.UtcNow, tz);
    }
}
