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

    /// <summary>The shared metrics meter instance.</summary>
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

    /// <summary>Counts jobs discarded because their deadline passed before execution.</summary>
    internal static readonly Counter<long> JobsExpired =
        Meter.CreateCounter<long>("nexjob.jobs.expired", "jobs", "Number of jobs discarded because their deadline passed before execution.");

    /// <summary>Records job execution duration in milliseconds.</summary>
    internal static readonly Histogram<double> JobDuration =
        Meter.CreateHistogram<double>("nexjob.job.duration", "ms", "Job execution duration in milliseconds.");

    /// <summary>Observable gauge for queue depth, tagged by queue name.</summary>
    internal static readonly ObservableGauge<long> QueueDepth =
        Meter.CreateObservableGauge<long>(
            "nexjob.queue.depth",
            () => ObserveQueueDepth(),
            "jobs",
            "Current number of enqueued jobs waiting in the queue.");

    /// <summary>Observable gauge for number of active workers currently executing jobs.</summary>
    internal static readonly ObservableGauge<int> WorkersActive =
        Meter.CreateObservableGauge<int>(
            "nexjob.workers.active",
            () => ObserveWorkersActive(),
            "workers",
            "Number of workers currently executing jobs.");

    /// <summary>Observable gauge for total number of configured worker slots.</summary>
    internal static readonly ObservableGauge<int> WorkersTotal =
        Meter.CreateObservableGauge<int>(
            "nexjob.workers.total",
            () => ObserveWorkersTotal(),
            "workers",
            "Total number of worker slots configured.");

    /// <summary>Synchronization lock used for safe provider registration and metric observation.</summary>
    internal static readonly object SyncLock = new();

    private static Func<int>? _activeWorkersProvider;
    private static Func<int>? _totalWorkersProvider;
    private static Func<IEnumerable<Measurement<long>>>? _queueDepthProvider;

    /// <summary>
    /// Configures the callback providers for active and total worker gauges.
    /// </summary>
    /// <param name="activeWorkersProvider">Delegate returning the current active workers count.</param>
    /// <param name="totalWorkersProvider">Delegate returning the total worker slots count.</param>
    public static void SetWorkerMetricsProviders(Func<int>? activeWorkersProvider, Func<int>? totalWorkersProvider)
    {
        lock (SyncLock)
        {
            _activeWorkersProvider = activeWorkersProvider;
            _totalWorkersProvider = totalWorkersProvider;
        }
    }

    /// <summary>
    /// Configures the callback provider for the queue depth gauge.
    /// </summary>
    /// <param name="queueDepthProvider">Delegate returning measurements of queue depth tagged by queue.</param>
    public static void SetQueueDepthProvider(Func<IEnumerable<Measurement<long>>>? queueDepthProvider)
    {
        lock (SyncLock)
        {
            _queueDepthProvider = queueDepthProvider;
        }
    }

    private static IEnumerable<Measurement<long>> ObserveQueueDepth()
    {
        lock (SyncLock)
        {
            try
            {
                return _queueDepthProvider?.Invoke() ?? [];
            }
            catch
            {
                return [];
            }
        }
    }

    private static int ObserveWorkersActive()
    {
        lock (SyncLock)
        {
            try
            {
                return _activeWorkersProvider?.Invoke() ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    private static int ObserveWorkersTotal()
    {
        lock (SyncLock)
        {
            try
            {
                return _totalWorkersProvider?.Invoke() ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
