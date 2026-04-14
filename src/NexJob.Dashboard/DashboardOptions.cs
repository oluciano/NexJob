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
}
