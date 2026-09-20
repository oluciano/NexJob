using Microsoft.Extensions.Diagnostics.HealthChecks;
using NexJob.Storage;

namespace NexJob;

/// <summary>
/// Health check for the NexJob background job system.
/// Reports <see cref="HealthStatus.Healthy"/> when the storage layer responds within
/// <see cref="NexJobOptions.HealthCheckTimeout"/>, <see cref="HealthStatus.Degraded"/> when
/// the dead-letter (failed) count exceeds <see cref="NexJobOptions.HealthCheckFailedThreshold"/>,
/// and <see cref="HealthStatus.Unhealthy"/> when the storage is unreachable or times out.
/// </summary>
public sealed class NexJobHealthCheck : IHealthCheck
{
    private readonly IDashboardStorage _storage;
    private readonly NexJobOptions _options;
    private readonly bool _hasExplicitOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="NexJobHealthCheck"/> class using default options.
    /// </summary>
    /// <param name="storage">The dashboard storage provider.</param>
    public NexJobHealthCheck(IDashboardStorage storage)
        : this(storage, new NexJobOptions(), hasExplicitOptions: false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="NexJobHealthCheck"/> class with the specified options.
    /// </summary>
    /// <param name="storage">The dashboard storage provider.</param>
    /// <param name="options">The NexJob configuration options.</param>
    public NexJobHealthCheck(IDashboardStorage storage, NexJobOptions options)
        : this(storage, options, hasExplicitOptions: true)
    {
    }

    private NexJobHealthCheck(IDashboardStorage storage, NexJobOptions options, bool hasExplicitOptions)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _hasExplicitOptions = hasExplicitOptions;
    }

    /// <summary>
    /// Gets or sets the number of failed (dead-letter) jobs above which the check reports
    /// <see cref="HealthStatus.Degraded"/>. Defaults to <c>100</c>.
    /// </summary>
    /// <remarks>
    /// Maintained for backward compatibility as a fallback when options are not explicitly configured.
    /// Prefer configuring <see cref="NexJobOptions.HealthCheckFailedThreshold"/>.
    /// </remarks>
    public static int FailedJobThreshold { get; set; } = 100;

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.HealthCheckTimeout);

            var metrics = await _storage.GetMetricsAsync(cts.Token).ConfigureAwait(false);

            var data = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["enqueued"] = metrics.Enqueued,
                ["processing"] = metrics.Processing,
                ["succeeded"] = metrics.Succeeded,
                ["failed"] = metrics.Failed,
                ["scheduled"] = metrics.Scheduled,
                ["recurring"] = metrics.Recurring,
            };

            var threshold = _hasExplicitOptions
                ? _options.HealthCheckFailedThreshold
                : FailedJobThreshold;

            if (metrics.Failed > threshold)
            {
                return HealthCheckResult.Degraded(
                    $"Dead-letter queue contains {metrics.Failed} failed jobs (threshold: {threshold}).",
                    data: data);
            }

            return HealthCheckResult.Healthy("NexJob storage is responsive.", data);
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(
                $"NexJob storage did not respond within {_options.HealthCheckTimeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} seconds.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("NexJob storage is unreachable.", ex);
        }
    }
}
