using Microsoft.Extensions.Logging;

namespace NexJob.Sample.WorkerService.Jobs;

/// <summary>
/// Parameterless maintenance job that can be triggered on-demand directly from the Job Catalog.
/// </summary>
public sealed class HealthCheckJob : IJob
{
    private readonly ILogger<HealthCheckJob> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HealthCheckJob"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public HealthCheckJob(ILogger<HealthCheckJob> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("System health check triggered from catalog. Verifying subsystem status...");
        await Task.Delay(250, cancellationToken);
        _logger.LogInformation("Subsystem checks OK. Memory and queues optimal.");
    }
}
