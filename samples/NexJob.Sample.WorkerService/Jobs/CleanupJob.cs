using Microsoft.Extensions.Logging;

namespace NexJob.Sample.WorkerService.Jobs;

/// <summary>
/// A sample job that simulates cleaning up resources.
/// </summary>
public sealed class CleanupJob : IJob<CleanupInput>
{
    private readonly ILogger<CleanupJob> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CleanupJob"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public CleanupJob(ILogger<CleanupJob> logger) => _logger = logger;

    /// <inheritdoc/>
    public async Task ExecuteAsync(CleanupInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cleaning up {Target}", input.Target);
        await Task.Delay(200, cancellationToken);
    }
}
