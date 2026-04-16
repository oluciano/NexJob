using Microsoft.Extensions.Diagnostics.HealthChecks;
using NexJob.Storage;

namespace NexJob;

/// <summary>
/// Health check for the NexJob background job system.
/// Reports <see cref="HealthStatus.Healthy"/> when the storage layer responds within 2 seconds,
/// <see cref="HealthStatus.Degraded"/> when the dead-letter (failed) count exceeds
/// <see cref="FailedJobThreshold"/>, and <see cref="HealthStatus.Unhealthy"/> when the
/// storage is unreachable.
/// </summary>
public sealed class NexJobHealthCheck : IHealthCheck
{
    private readonly IDashboardStorage _storage;

    /// <summary>Initializes a new <see cref="NexJobHealthCheck"/>.</summary>
    public NexJobHealthCheck(IDashboardStorage storage)
    {
        _storage = storage;
    }

    /// <summary>
    /// Number of failed (dead-letter) jobs above which the check reports
    /// <see cref="HealthStatus.Degraded"/>. Defaults to <c>100</c>.
    /// </summary>
    public static int FailedJobThreshold { get; set; } = 100;

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));

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

            if (metrics.Failed > FailedJobThreshold)
            {
                return HealthCheckResult.Degraded(
                    $"Dead-letter queue contains {metrics.Failed} failed jobs (threshold: {FailedJobThreshold}).",
                    data: data);
            }

            return HealthCheckResult.Healthy("NexJob storage is responsive.", data);
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("NexJob storage did not respond within 2 seconds.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("NexJob storage is unreachable.", ex);
        }
    }
}
