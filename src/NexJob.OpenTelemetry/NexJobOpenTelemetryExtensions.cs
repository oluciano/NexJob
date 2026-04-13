using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace NexJob.OpenTelemetry;

/// <summary>
/// Extension methods for adding NexJob instrumentation to the OpenTelemetry SDK.
/// </summary>
public static class NexJobOpenTelemetryExtensions
{
    /// <summary>
    /// Adds NexJob tracing instrumentation to the <see cref="TracerProviderBuilder"/>.
    /// Registers the NexJob <see cref="System.Diagnostics.ActivitySource"/> so that
    /// job enqueue and execution spans are captured.
    /// </summary>
    /// <param name="builder">The tracer provider builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static TracerProviderBuilder AddNexJobInstrumentation(
        this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Register the NexJob ActivitySource by name
        return builder.AddSource(NexJob.Telemetry.NexJobActivitySource.Name);
    }

    /// <summary>
    /// Adds NexJob metrics instrumentation to the <see cref="MeterProviderBuilder"/>.
    /// Registers the NexJob <see cref="System.Diagnostics.Metrics.Meter"/> so that
    /// job counters and histograms are collected.
    /// </summary>
    /// <param name="builder">The meter provider builder.</param>
    /// <returns>The builder for chaining.</returns>
    public static MeterProviderBuilder AddNexJobInstrumentation(
        this MeterProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Register the NexJob Meter by name
        return builder.AddMeter(NexJob.Telemetry.NexJobMetrics.MeterName);
    }
}
