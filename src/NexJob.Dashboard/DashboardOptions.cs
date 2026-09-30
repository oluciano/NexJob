namespace NexJob.Dashboard;

/// <summary>Configuration options for the NexJob dashboard middleware.</summary>
public sealed class DashboardOptions
{
    private readonly List<DashboardCluster> _clusters = [];

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

    /// <summary>
    /// Gets the collection of registered federated clusters.
    /// When empty (default), the dashboard operates in single-cluster mode querying services from DI.
    /// When one or more clusters are registered, multi-cluster federation is enabled.
    /// </summary>
    public IReadOnlyList<DashboardCluster> Clusters => _clusters;

    /// <summary>
    /// Registers a federated cluster in the dashboard.
    /// </summary>
    /// <param name="cluster">The cluster descriptor to register.</param>
    /// <returns>This <see cref="DashboardOptions"/> instance for chaining.</returns>
    public DashboardOptions AddCluster(DashboardCluster cluster)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        if (_clusters.Exists(c => string.Equals(c.Id, cluster.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"A cluster with ID '{cluster.Id}' is already registered.");
        }

        _clusters.Add(cluster);
        return this;
    }
}
