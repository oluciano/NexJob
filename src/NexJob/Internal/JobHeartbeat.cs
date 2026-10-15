using Microsoft.Extensions.Logging;
using NexJob.Storage;

namespace NexJob.Internal;

/// <summary>
/// Refreshes the heartbeat of a running job on an interval, so the orphan watcher does not
/// re-run a job that is healthy but slow.
/// </summary>
internal sealed class JobHeartbeat
{
    private readonly IJobStorage _storage;
    private readonly NexJobOptions _options;
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobHeartbeat"/> class.
    /// </summary>
    /// <param name="storage">The job storage that records the heartbeat.</param>
    /// <param name="options">The options that provide the heartbeat interval, read on every tick.</param>
    /// <param name="logger">The logger used to report a failed heartbeat update.</param>
    public JobHeartbeat(IJobStorage storage, NexJobOptions options, ILogger logger)
    {
        _storage = storage;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Updates the heartbeat of <paramref name="jobId"/> every heartbeat interval until
    /// <paramref name="cancellationToken"/> is cancelled. A storage error never ends the loop.
    /// </summary>
    /// <param name="jobId">The running job.</param>
    /// <param name="cancellationToken">Cancelled when the job completes.</param>
    /// <returns>A task that completes when the token is cancelled.</returns>
    public async Task RunAsync(JobId jobId, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_options.HeartbeatInterval, cancellationToken).ConfigureAwait(false);
                try
                {
                    await _storage.UpdateHeartbeatAsync(jobId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A transient storage error must not end the heartbeat, or the orphan watcher would re-run a healthy job.
                    _logger.LogWarning(ex, "Heartbeat update for job {JobId} failed; will retry on the next interval", jobId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on job completion
        }
    }
}
