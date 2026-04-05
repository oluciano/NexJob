using NexJob.Configuration;

namespace NexJob;

/// <summary>
/// Configuration options for the NexJob background job system.
/// Pass an <see cref="Action{NexJobOptions}"/> to <c>AddNexJob</c> to customise these values.
/// </summary>
public sealed class NexJobOptions
{
    /// <summary>
    /// Maximum number of jobs that can execute concurrently on this host.
    /// Defaults to <c>10</c>.
    /// </summary>
    /// <remarks>
    /// Each worker runs in its own <see cref="System.Threading.Tasks.Task"/>.
    /// Setting this higher than your storage connection pool size may cause contention.
    /// For CPU-bound jobs, values above <c>Environment.ProcessorCount</c> rarely help.
    /// </remarks>
    public int Workers { get; set; } = 10;

    /// <summary>
    /// Optional identifier for the server/node. If null, MachineName + Guid is used.
    /// </summary>
    public string? ServerId { get; set; }

    /// <summary>
    /// Maximum number of execution attempts before a job is moved to the dead-letter
    /// (failed) state. Defaults to <c>10</c>.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>
    /// How often the dispatcher polls storage for new jobs when none are immediately available.
    /// Defaults to <c>15 seconds</c>.
    /// </summary>
    /// <remarks>
    /// Local enqueues via <see cref="IScheduler"/> wake the dispatcher immediately —
    /// polling only affects jobs enqueued from external processes or other nodes.
    /// Reduce this for multi-node setups where latency matters.
    /// </remarks>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How often active workers refresh their heartbeat timestamp.
    /// Defaults to <c>30 seconds</c>.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often the server node refreshes its own global heartbeat.
    /// Defaults to <c>15 seconds</c>.
    /// </summary>
    public TimeSpan ServerHeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Maximum time allowed between heartbeat updates before a job is considered
    /// orphaned and re-enqueued. Defaults to <c>5 minutes</c>.
    /// </summary>
    /// <remarks>
    /// If a worker crashes mid-execution, its job stays in <see cref="JobStatus.Processing"/>
    /// until the orphan watcher detects the stale heartbeat and re-enqueues it.
    /// Set this higher than the longest expected job duration to avoid false re-enqueues.
    /// </remarks>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum time to wait for active jobs to complete during graceful shutdown.
    /// Jobs still running after this timeout are left for the orphan watcher to requeue.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Ordered list of queue names that workers on this host will poll.
    /// Queues are drained in the order specified. Defaults to <c>["default"]</c>.
    /// </summary>
    public IReadOnlyList<string> Queues { get; set; } = ["default"];

    /// <summary>
    /// Computes the retry delay for a failed job given the attempt number (1-based).
    /// Defaults to exponential backoff: <c>pow(attempt, 4) + 15 + rand(30) × (attempt + 1)</c> seconds.
    /// </summary>
    /// <remarks>
    /// Override this in tests to eliminate delays:
    /// <code>opt.RetryDelayFactory = _ => TimeSpan.Zero;</code>
    /// Override in production for custom backoff strategies (linear, fixed, circuit-breaker, etc.).
    /// This property cannot be set via <c>appsettings.json</c> — code only.
    /// </remarks>
    public Func<int, TimeSpan> RetryDelayFactory { get; set; } = attempt =>
        TimeSpan.FromSeconds(Math.Pow(attempt, 4) + 15 + (System.Security.Cryptography.RandomNumberGenerator.GetInt32(30) * (attempt + 1)));

    /// <summary>
    /// Dashboard-specific settings.
    /// </summary>
    public DashboardSettings Dashboard { get; set; } = new();

    /// <summary>
    /// Maximum number of log lines captured per job execution. Defaults to <c>200</c>.
    /// </summary>
    public int MaxJobLogLines { get; set; } = 200;

    /// <summary>
    /// Per-queue settings loaded from <c>appsettings.json</c>, used for execution windows.
    /// Populated by <see cref="ApplySettings"/>.
    /// </summary>
    public List<QueueSettings> QueueSettings { get; set; } = [];

    /// <summary>
    /// Recurring job settings loaded from <c>appsettings.json</c>.
    /// Populated by <see cref="ApplySettings"/>.
    /// </summary>
    public List<RecurringJobSettings> RecurringJobs { get; set; } = [];

    /// <summary>
    /// Internal flag indicating whether a storage provider has been explicitly configured.
    /// </summary>
    internal bool StorageConfigured { get; set; }

    /// <summary>
    /// Marks the in-memory storage provider as explicitly configured, enabling fluent chaining.
    /// </summary>
    /// <returns>This options instance for method chaining.</returns>
    public NexJobOptions UseInMemory()
    {
        StorageConfigured = true;
        return this;
    }

    /// <summary>
    /// Applies values from a <see cref="NexJobSettings"/> instance (typically loaded from
    /// <c>appsettings.json</c>) onto this options object.
    /// <see cref="RetryDelayFactory"/> is intentionally not overwritten — it can only be
    /// set via code.
    /// </summary>
    internal void ApplySettings(NexJobSettings s)
    {
        Workers = s.Workers;
        MaxAttempts = s.MaxAttempts;
        MaxJobLogLines = s.MaxJobLogLines;
        PollingInterval = s.PollingInterval;
        HeartbeatInterval = s.HeartbeatInterval;
        ServerHeartbeatInterval = s.ServerHeartbeatInterval;
        HeartbeatTimeout = s.HeartbeatTimeout;
        ServerId = s.ServerId;
        QueueSettings = s.QueueSettings;
        RecurringJobs = s.RecurringJobs;
        if (s.Queues.Length > 0)
        {
            Queues = s.Queues;
        }

        if (s.ShutdownTimeoutSeconds > 0)
        {
            ShutdownTimeout = TimeSpan.FromSeconds(s.ShutdownTimeoutSeconds);
        }

        Dashboard = s.Dashboard;
    }
}
