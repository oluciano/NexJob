using NexJob.Configuration;
using NexJob.Storage;

namespace NexJob.Dashboard;

/// <summary>
/// Represents an isolated cluster registered in the dashboard for multi-cluster federation.
/// </summary>
public sealed class DashboardCluster
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardCluster"/> class.
    /// </summary>
    /// <param name="id">Unique identifier for the cluster (used in URL queries and routing).</param>
    /// <param name="name">Human-readable display name for the cluster selector dropdown.</param>
    /// <param name="dashboardStorage">Dashboard storage contract for querying jobs and metrics.</param>
    /// <param name="jobStorage">Optional job execution storage for querying active servers and health.</param>
    /// <param name="recurringStorage">Optional recurring storage for inspecting and controlling cron schedules.</param>
    /// <param name="controlService">Optional job control service for requeue, delete, and pause actions.</param>
    /// <param name="runtimeStore">Optional runtime settings store for queue pausing and throttling.</param>
    /// <param name="queues">Optional list of queues scoped to this cluster.</param>
    /// <param name="isReadOnly">When <see langword="true"/>, disables all mutation actions on this cluster.</param>
    public DashboardCluster(
        string id,
        string name,
        IDashboardStorage dashboardStorage,
        IJobStorage? jobStorage = null,
        IRecurringStorage? recurringStorage = null,
        IJobControlService? controlService = null,
        IRuntimeSettingsStore? runtimeStore = null,
        IReadOnlyList<string>? queues = null,
        bool isReadOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(dashboardStorage);

        Id = id.Trim();
        Name = name.Trim();
        DashboardStorage = dashboardStorage;
        JobStorage = jobStorage ?? (dashboardStorage as IJobStorage);
        RecurringStorage = recurringStorage ?? (dashboardStorage as IRecurringStorage);
        ControlService = controlService;
        RuntimeStore = runtimeStore;
        Queues = queues;
        IsReadOnly = isReadOnly;
    }

    /// <summary>
    /// Gets the unique identifier for the cluster.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the human-readable display name for the cluster.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the dashboard storage instance for querying metrics and jobs.
    /// </summary>
    public IDashboardStorage DashboardStorage { get; }

    /// <summary>
    /// Gets the job storage instance for querying servers and worker nodes.
    /// </summary>
    public IJobStorage? JobStorage { get; }

    /// <summary>
    /// Gets the recurring job storage instance.
    /// </summary>
    public IRecurringStorage? RecurringStorage { get; }

    /// <summary>
    /// Gets the job control service for requeue and delete operations.
    /// </summary>
    public IJobControlService? ControlService { get; }

    /// <summary>
    /// Gets the runtime settings store for queue pause/resume operations.
    /// </summary>
    public IRuntimeSettingsStore? RuntimeStore { get; }

    /// <summary>
    /// Gets the optional collection of queue names scoped to this cluster.
    /// </summary>
    public IReadOnlyList<string>? Queues { get; }

    /// <summary>
    /// Gets a value indicating whether administrative mutations are disabled for this cluster.
    /// </summary>
    public bool IsReadOnly { get; }
}
