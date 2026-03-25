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
    private readonly IStorageProvider _storage;
    private readonly NexJobOptions _options;
    private readonly ILogger<OrphanedJobWatcherService> _logger;

    /// <summary>
    /// Initializes a new <see cref="OrphanedJobWatcherService"/>.
    /// </summary>
    public OrphanedJobWatcherService(
        IStorageProvider storage,
        NexJobOptions options,
        ILogger<OrphanedJobWatcherService> logger)
    {
        _storage = storage;
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
                await _storage.RequeueOrphanedJobsAsync(_options.HeartbeatTimeout, stoppingToken);
                // Check once per heartbeat timeout period — any more frequent is redundant
                await Task.Delay(_options.HeartbeatTimeout, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OrphanedJobWatcherService");
            }
        }

        _logger.LogInformation("OrphanedJobWatcherService stopped.");
    }
}
