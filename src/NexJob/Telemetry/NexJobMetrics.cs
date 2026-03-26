using System.Diagnostics.Metrics;
using System.Reflection;

namespace NexJob.Telemetry;

/// <summary>
/// Exposes the NexJob <see cref="Meter"/> and its instruments.
/// Register with the OpenTelemetry SDK via <c>AddMeter(NexJobMetrics.MeterName)</c>.
/// </summary>
public static class NexJobMetrics
{
    /// <summary>The meter name for use with the OpenTelemetry SDK.</summary>
    public const string MeterName = "NexJob";

    internal static readonly Meter Meter =
        new(MeterName, Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0");

    /// <summary>Counts jobs enqueued.</summary>
    internal static readonly Counter<long> JobsEnqueued =
        Meter.CreateCounter<long>("nexjob.jobs.enqueued", "jobs", "Number of jobs enqueued.");

    /// <summary>Counts jobs completed successfully.</summary>
    internal static readonly Counter<long> JobsSucceeded =
        Meter.CreateCounter<long>("nexjob.jobs.succeeded", "jobs", "Number of jobs completed successfully.");

    /// <summary>Counts jobs that failed (including those moved to dead-letter).</summary>
    internal static readonly Counter<long> JobsFailed =
        Meter.CreateCounter<long>("nexjob.jobs.failed", "jobs", "Number of jobs that failed.");

    /// <summary>Records job execution duration in milliseconds.</summary>
    internal static readonly Histogram<double> JobDuration =
        Meter.CreateHistogram<double>("nexjob.job.duration", "ms", "Job execution duration in milliseconds.");
}
