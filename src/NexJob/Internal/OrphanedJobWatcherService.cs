using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Storage;

namespace NexJob.Internal;

/// <summary>
/// Hosted background service that periodically scans for jobs stuck in
/// <see cref="JobStatus.Processing"/> with an expired heartbeat and re-enqueues
/// them so that a healthy worker can retry execution.
/// </summary>
internal sealed class OrphanedJobWatcherService : BackgroundService
{
    private readonly IJobStorage _storage;
    private readonly IDashboardStorage? _dashboard;
    private readonly IDeadLetterDispatcher? _deadLetterDispatcher;
    private readonly NexJobOptions _options;
    private readonly ILogger<OrphanedJobWatcherService> _logger;

    /// <summary>
    /// Initializes a new <see cref="OrphanedJobWatcherService"/> that requeues orphans but does not dead-letter them.
    /// </summary>
    public OrphanedJobWatcherService(
        IJobStorage storage,
        NexJobOptions options,
        ILogger<OrphanedJobWatcherService> logger)
        : this(storage, null, null, options, logger)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="OrphanedJobWatcherService"/> that also runs dead-letter handling for the jobs the
    /// storage reports as failed (see <see cref="IOrphanedJobReporter"/>).
    /// </summary>
    [ActivatorUtilitiesConstructor]
    public OrphanedJobWatcherService(
        IJobStorage storage,
        IDashboardStorage? dashboard,
        IDeadLetterDispatcher? deadLetterDispatcher,
        NexJobOptions options,
        ILogger<OrphanedJobWatcherService> logger)
    {
        _storage = storage;
        _dashboard = dashboard;
        _deadLetterDispatcher = deadLetterDispatcher;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "OrphanedJobWatcherService started. Heartbeat timeout: {Timeout}.",
            _options.HeartbeatTimeout);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_storage is IOrphanedJobReporter reporter && _dashboard is not null && _deadLetterDispatcher is not null)
                {
                    var failed = await reporter.RequeueOrphanedJobsAndReportAsync(_options.HeartbeatTimeout, stoppingToken).ConfigureAwait(false);
                    foreach (var jobId in failed)
                    {
                        await DeadLetterAsync(_dashboard, _deadLetterDispatcher, jobId).ConfigureAwait(false);
                    }
                }
                else
                {
                    await _storage.RequeueOrphanedJobsAsync(_options.HeartbeatTimeout, stoppingToken).ConfigureAwait(false);
                }

                // Check once per heartbeat timeout period — any more frequent is redundant
                await Task.Delay(_options.HeartbeatTimeout, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OrphanedJobWatcherService");
                // Cap backoff to HeartbeatTimeout so fast test loops are not stalled.
                var errorDelay = TimeSpan.FromSeconds(5) < _options.HeartbeatTimeout
                    ? TimeSpan.FromSeconds(5)
                    : _options.HeartbeatTimeout;
                await Task.Delay(errorDelay, stoppingToken).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("OrphanedJobWatcherService stopped.");
    }

    // The job is already Failed in storage when it gets here, so nothing in this method may stop the watcher: a handler
    // that throws, a job that was purged in the meantime or a storage error only costs a log line.
    private async Task DeadLetterAsync(IDashboardStorage dashboard, IDeadLetterDispatcher dispatcher, JobId jobId)
    {
        try
        {
            var job = await dashboard.GetJobByIdAsync(jobId, CancellationToken.None).ConfigureAwait(false);
            if (job is null)
            {
                return;
            }

            _logger.LogWarning(
                "Job {JobId} failed because the node running it stopped sending heartbeats and no attempts were left ({Attempts} used).",
                jobId,
                job.Attempts);

            await dispatcher
                .DispatchAsync(job, new OrphanedJobException(jobId, job.Attempts), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dead-letter handling failed for orphaned job {JobId} — the watcher continues.", jobId);
        }
    }
}
