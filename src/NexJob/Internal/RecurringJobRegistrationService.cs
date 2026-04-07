using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Configuration;

namespace NexJob.Internal;

/// <summary>
/// Hosted service that registers recurring jobs from configuration at startup.
/// </summary>
internal sealed class RecurringJobRegistrationService : BackgroundService
{
    private readonly NexJobOptions _options;
    private readonly RecurringJobRegistrar _registrar;
    private readonly ILogger<RecurringJobRegistrationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecurringJobRegistrationService"/> class.
    /// </summary>
    /// <param name="options">The NexJob options.</param>
    /// <param name="registrar">The recurring job registrar.</param>
    /// <param name="logger">The logger.</param>
    public RecurringJobRegistrationService(
        NexJobOptions options,
        RecurringJobRegistrar registrar,
        ILogger<RecurringJobRegistrationService> logger)
    {
        _options = options;
        _registrar = registrar;
        _logger = logger;
    }

    /// <summary>
    /// Registers recurring jobs from configuration at startup.
    /// </summary>
    /// <param name="stoppingToken">Triggered when the host is shutting down.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting recurring job registration from configuration...");

        try
        {
            await _registrar.RegisterRecurringJobsAsync(_options.RecurringJobs, stoppingToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Registered {Count} recurring jobs from configuration.",
                _registrar.RegisteredJobIds.Count);

            // Log details about each registered job
            foreach (var jobId in _registrar.RegisteredJobIds)
            {
                _logger.LogDebug("Registered recurring job: {JobId}", jobId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register recurring jobs from configuration");
        }
    }
}
