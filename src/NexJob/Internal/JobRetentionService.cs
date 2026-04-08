using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Configuration;
using NexJob.Storage;

namespace NexJob.Internal;

/// <summary>
/// Hosted background service that periodically purges terminal jobs
/// (<see cref="JobStatus.Succeeded"/>, <see cref="JobStatus.Failed"/>,
/// <see cref="JobStatus.Expired"/>) older than the configured retention thresholds.
/// Thresholds are read from <see cref="IRuntimeSettingsStore"/> on every cycle so that
/// dashboard overrides take effect without a restart.
/// </summary>
internal sealed class JobRetentionService : BackgroundService
{
    private readonly IStorageProvider _storage;
    private readonly IRuntimeSettingsStore _runtimeStore;
    private readonly NexJobOptions _options;
    private readonly ILogger<JobRetentionService> _logger;

    /// <summary>Initializes a new <see cref="JobRetentionService"/>.</summary>
    public JobRetentionService(
        IStorageProvider storage,
        IRuntimeSettingsStore runtimeStore,
        NexJobOptions options,
        ILogger<JobRetentionService> logger)
    {
        _storage = storage;
        _runtimeStore = runtimeStore;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "JobRetentionService started. Interval: {Interval}. " +
            "Defaults — Succeeded: {Succeeded}d, Failed: {Failed}d, Expired: {Expired}d.",
            _options.RetentionInterval,
            _options.RetentionSucceeded.TotalDays,
            _options.RetentionFailed.TotalDays,
            _options.RetentionExpired.TotalDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.RetentionInterval, stoppingToken).ConfigureAwait(false);

                var runtime = await _runtimeStore.GetAsync(stoppingToken).ConfigureAwait(false);

                var policy = new RetentionPolicy
                {
                    RetainSucceeded = runtime.RetentionSucceeded ?? _options.RetentionSucceeded,
                    RetainFailed = runtime.RetentionFailed ?? _options.RetentionFailed,
                    RetainExpired = runtime.RetentionExpired ?? _options.RetentionExpired,
                };

                var deleted = await _storage.PurgeJobsAsync(policy, stoppingToken).ConfigureAwait(false);

                if (deleted > 0)
                {
                    _logger.LogInformation(
                        "JobRetentionService purged {Count} terminal job(s).", deleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in JobRetentionService.");
            }
        }

        _logger.LogInformation("JobRetentionService stopped.");
    }
}
