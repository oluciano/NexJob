using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace NexJob;

/// <summary>
/// Extension methods for registering the NexJob health check.
/// </summary>
public static class NexJobHealthCheckExtensions
{
    /// <summary>
    /// Adds the <see cref="NexJobHealthCheck"/> to the health check pipeline.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The health check name. Defaults to <c>nexjob</c>.</param>
    /// <param name="failureStatus">
    /// The <see cref="HealthStatus"/> to report when unhealthy.
    /// Defaults to <see cref="HealthStatus.Unhealthy"/>.
    /// </param>
    /// <param name="tags">Optional tags to associate with the health check.</param>
    /// <returns>The original builder for chaining.</returns>
    public static IHealthChecksBuilder AddNexJob(
        this IHealthChecksBuilder builder,
        string name = "nexjob",
        HealthStatus failureStatus = HealthStatus.Unhealthy,
        IEnumerable<string>? tags = null)
    {
        return builder.Add(new HealthCheckRegistration(
            name,
            sp => sp.GetRequiredService<NexJobHealthCheck>(),
            failureStatus,
            tags));
    }
}
