namespace NexJob.Dashboard;

/// <summary>Configuration options for the NexJob dashboard middleware.</summary>
public sealed class DashboardOptions
{
    /// <summary>Title shown in the browser tab and sidebar header. Defaults to <c>NexJob</c>.</summary>
    public string Title { get; set; } = "NexJob";

    /// <summary>
    /// Time-to-live for cached metrics. Set to <see cref="TimeSpan.Zero"/> to disable caching.
    /// Defaults to 3 seconds to prevent excessive database load during SSE polling.
    /// </summary>
    public TimeSpan MetricsCacheTtl { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Optional collection of queue names to scope the dashboard view to.
    /// When specified, navigation counters, queue cards, and default job listings
    /// are scoped exclusively to these queues.
    /// When <see langword="null"/> (default), all queues across the cluster are visible.
    /// </summary>
    public IReadOnlyList<string>? Queues { get; set; }
}
