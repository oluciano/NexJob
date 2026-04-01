using NexJob;

namespace TesteNextJob.Jobs;

/// <summary>
/// Simple job that logs the current time. Implements IJob (no input required).
/// </summary>
public sealed class TesteOnlyJob(ILogger<TesteOnlyJob> logger) : IJob
{
    /// <summary>
    /// Executes the job, logging the current time to both logger and console.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        logger.LogInformation("[TesteOnlyJob] Executing at {Time}", now);
        Console.WriteLine($"[TesteOnlyJob] Current time: {now}");
        await Task.CompletedTask;
    }
}
