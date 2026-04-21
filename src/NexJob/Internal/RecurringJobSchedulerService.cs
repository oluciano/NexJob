using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Configuration;
using NexJob.Storage;

namespace NexJob.Internal;

/// <summary>
/// Hosted background service that polls the storage provider for due recurring jobs,
/// enqueues them as normal <see cref="JobRecord"/> instances, and advances their
/// <see cref="RecurringJobRecord.NextExecution"/> timestamp.
/// </summary>
internal sealed class RecurringJobSchedulerService : BackgroundService
{
    private readonly IJobStorage _jobStorage;
    private readonly IRecurringStorage _recurringStorage;
    private readonly IRuntimeSettingsStore _runtimeStore;
    private readonly NexJobOptions _options;
    private readonly ILogger<RecurringJobSchedulerService> _logger;

    /// <summary>
    /// Initializes a new <see cref="RecurringJobSchedulerService"/>.
    /// </summary>
    public RecurringJobSchedulerService(
        IJobStorage jobStorage,
        IRecurringStorage recurringStorage,
        IRuntimeSettingsStore runtimeStore,
        NexJobOptions options,
        ILogger<RecurringJobSchedulerService> logger)
    {
        _jobStorage = jobStorage;
        _recurringStorage = recurringStorage;
        _runtimeStore = runtimeStore;
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
                var runtime = await _runtimeStore.GetAsync(stoppingToken).ConfigureAwait(false);
                if (runtime.RecurringJobsPaused)
                {
                    _logger.LogDebug("Recurring jobs are globally paused; skipping scheduling cycle.");
                    await Task.Delay(runtime.PollingInterval ?? _options.PollingInterval, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                await EnqueueDueJobsAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(runtime.PollingInterval ?? _options.PollingInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in RecurringJobSchedulerService");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("RecurringJobSchedulerService stopped.");
    }

    // ─── private ─────────────────────────────────────────────────────────────

    private static DateTimeOffset? CalculateNextExecution(RecurringJobRecord recurring)
    {
        var tz = recurring.TimeZoneId is not null
            ? TimeZoneInfo.FindSystemTimeZoneById(recurring.TimeZoneId)
            : TimeZoneInfo.Utc;

        var effectiveCron = recurring.CronOverride ?? recurring.Cron;
        var cronExpression = DefaultScheduler.ParseCron(effectiveCron);
        return cronExpression.GetNextOccurrence(DateTimeOffset.UtcNow, tz);
    }

    private async Task EnqueueDueJobsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var dueJobs = await _recurringStorage.GetDueRecurringJobsAsync(now, cancellationToken).ConfigureAwait(false);

        foreach (var recurring in dueJobs)
        {
            if (recurring.DeletedByUser)
            {
                _logger.LogDebug("Recurring job '{Id}' was deleted by user, skipping.", recurring.RecurringJobId);
                continue;
            }

            if (!recurring.Enabled)
            {
                _logger.LogDebug("Skipping disabled recurring job '{Id}'.", recurring.RecurringJobId);
                continue;
            }

            // Acquire distributed lock — prevents duplicate firing in multi-instance deployments.
            // TTL covers the full polling interval so a slow instance doesn't re-fire before
            // NextExecution is advanced.
            var lockTtl = (_options.PollingInterval * 2) + TimeSpan.FromSeconds(5);
            var acquired = await _recurringStorage
                .TryAcquireRecurringJobLockAsync(recurring.RecurringJobId, lockTtl, cancellationToken)
                .ConfigureAwait(false);

            if (!acquired)
            {
                _logger.LogDebug(
                    "Recurring job '{Id}' lock not acquired — another instance is handling it.",
                    recurring.RecurringJobId);
                continue;
            }

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
                    RecurringJobId = recurring.RecurringJobId,
                    // SkipIfRunning: idempotency key blocks a second instance while
                    // the first is Enqueued or Processing.
                    // AllowConcurrent: no key — every firing creates a new instance.
                    IdempotencyKey = recurring.ConcurrencyPolicy == RecurringConcurrencyPolicy.SkipIfRunning
                        ? $"recurring:{recurring.RecurringJobId}"
                        : null,
                };

                await _jobStorage.EnqueueAsync(jobRecord, DuplicatePolicy.AllowAfterFailed, cancellationToken).ConfigureAwait(false);

                var nextExecution = CalculateNextExecution(recurring);

                if (nextExecution.HasValue)
                {
                    await _recurringStorage.SetRecurringJobNextExecutionAsync(
                        recurring.RecurringJobId, nextExecution.Value, cancellationToken).ConfigureAwait(false);
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
            finally
            {
                await _recurringStorage
                    .ReleaseRecurringJobLockAsync(recurring.RecurringJobId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
