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
    /// Maximum time a job may hold a distributed throttle slot in Redis.
    /// A node keeps refreshing the slots of its running jobs, so a slot left behind by a crashed node is
    /// reclaimed after three <see cref="HeartbeatInterval"/> periods; this value only caps how long a live job
    /// can keep a slot (a slot older than this is no longer refreshed and expires).
    /// Must be greater than the longest expected job execution time.
    /// Defaults to 1 hour.
    /// </summary>
    public TimeSpan DistributedThrottleTtl { get; set; } = TimeSpan.FromHours(1);

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
    /// Delay applied when deferring a foreign job (a job whose type or input type is not loaded
    /// in the current application runtime). This releases the job back to storage so another node
    /// that owns the job type can execute it, while preventing immediate hot-loop re-fetching.
    /// Defaults to <c>5 seconds</c>.
    /// </summary>
    public TimeSpan ForeignJobRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

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
    /// Maximum time allowed for storage health check probes to respond before reporting
    /// <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy"/>.
    /// Defaults to <c>5 seconds</c>.
    /// </summary>
    public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Number of failed (dead-letter) jobs above which the health check reports
    /// <see cref="Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded"/>.
    /// Defaults to <c>100</c>.
    /// </summary>
    public int HealthCheckFailedThreshold { get; set; } = 100;

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
    /// When enabled, completed jobs are acknowledged in asynchronous batches to reduce database write roundtrips.
    /// Defaults to <c>false</c> (synchronous commit on job completion).
    /// </summary>
    public bool EnableBatchAcknowledgment { get; set; }

    /// <summary>
    /// Maximum number of log lines captured per job execution. Defaults to <c>200</c>.
    /// </summary>
    public int MaxJobLogLines { get; set; } = 200;

    /// <summary>
    /// How long to retain <see cref="JobStatus.Succeeded"/> jobs before automatic deletion.
    /// Defaults to 7 days. Set to <see cref="TimeSpan.Zero"/> to disable purging for this status.
    /// </summary>
    public TimeSpan RetentionSucceeded { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How long to retain <see cref="JobStatus.Failed"/> jobs before automatic deletion.
    /// Defaults to 30 days. Set to <see cref="TimeSpan.Zero"/> to disable purging for this status.
    /// </summary>
    public TimeSpan RetentionFailed { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    /// How long to retain <see cref="JobStatus.Expired"/> jobs before automatic deletion.
    /// Defaults to 7 days. Set to <see cref="TimeSpan.Zero"/> to disable purging for this status.
    /// </summary>
    public TimeSpan RetentionExpired { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How long to retain dead-letter jobs before automatic deletion.
    /// Defaults to 60 days. Set to <see cref="TimeSpan.Zero"/> to disable purging for this status.
    /// </summary>
    public TimeSpan RetentionDeadLetter { get; set; } = TimeSpan.FromDays(60);

    /// <summary>
    /// How often the retention service runs to purge old terminal jobs.
    /// Defaults to 1 hour.
    /// </summary>
    public TimeSpan RetentionInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Maximum number of rows to delete per batch/chunk during retention purge operations.
    /// Helps avoid database lock escalation and transaction log bloat.
    /// Defaults to 1000.
    /// </summary>
    public int RetentionBatchSize { get; set; } = 1000;

    /// <summary>
    /// Per-queue settings loaded from <c>appsettings.json</c>, used for execution windows and circuit breakers.
    /// Populated by <see cref="ApplySettings"/> or configured programmatically via <see cref="ConfigureQueue"/>.
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
    /// Configures queue-level behavior, such as execution windows or dynamic circuit breakers.
    /// </summary>
    /// <param name="queueName">The name of the queue to configure.</param>
    /// <param name="configure">The configuration delegate.</param>
    /// <returns>This options instance for method chaining.</returns>
    public NexJobOptions ConfigureQueue(string queueName, Action<QueueSettings> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ArgumentNullException.ThrowIfNull(configure);

        var existing = QueueSettings.Find(q => string.Equals(q.Name, queueName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new QueueSettings { Name = queueName };
            QueueSettings.Add(existing);
        }

        configure(existing);
        return this;
    }

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
        HealthCheckTimeout = s.HealthCheckTimeout;
        HealthCheckFailedThreshold = s.HealthCheckFailedThreshold;
    }
}
