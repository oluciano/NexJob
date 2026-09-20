using NexJob;

namespace NexJob.Sample.ConfiguredRecurring.Jobs;

/// <summary>
/// Simple heartbeat job that logs the current UTC time. Implements IJob (no input required).
/// </summary>
public sealed class HeartbeatJob(ILogger<HeartbeatJob> logger) : IJob
{
    /// <summary>
    /// Executes the job, logging the current UTC time to both logger and console.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        logger.LogInformation("[HeartbeatJob] Executing at {Time} UTC", now);
        Console.WriteLine($"[HeartbeatJob] Current UTC time: {now}");
        await Task.CompletedTask;
    }
}
