using NexJob.Configuration;
using NexJob.Internal;

namespace NexJob;

/// <summary>
/// Configuration options for the NexJob background job system.
/// Pass an <see cref="Action{NexJobOptions}"/> to <c>AddNexJob</c> to customise these values.
/// </summary>
public sealed class NexJobOptions
{
    private const int MaxQueuePrefixLength = 100;
    private TimeSpan? _defaultExecutionTimeout;
    private string? _queuePrefix;
    private int _workers = 10;
    private IReadOnlyList<Type> _ignoreRetryAttemptExceptions = [];

    /// <summary>
    /// Maximum number of jobs that can execute concurrently on this host.
    /// Defaults to <c>10</c>. <c>0</c> means that this host does not fetch or execute jobs (for example a dashboard-only host);
    /// it still registers as a node, with no workers and no polled queues. A negative value is rejected.
    /// </summary>
    /// <remarks>
    /// Each worker runs in its own <see cref="System.Threading.Tasks.Task"/>.
    /// Setting this higher than your storage connection pool size may cause contention.
    /// For CPU-bound jobs, values above <c>Environment.ProcessorCount</c> rarely help.
    /// </remarks>
    public int Workers
    {
        get => _workers;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _workers = value;
        }
    }

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
    /// Maximum time a job may run when its type has no <see cref="ExecutionTimeoutAttribute"/>.
    /// <see langword="null"/> (the default) means unbounded. Cancellation is cooperative: a job that ignores its
    /// <see cref="CancellationToken"/> keeps its worker slot. Must be greater than zero when set.
    /// </summary>
    public TimeSpan? DefaultExecutionTimeout
    {
        get => _defaultExecutionTimeout;
        set
        {
            if (value is { } timeout)
            {
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
            }

            _defaultExecutionTimeout = value;
        }
    }

    /// <summary>
    /// Exception types that no job should retry, for example <see cref="ArgumentException"/>: the input will not
    /// change, so another attempt only wastes a worker. A run that fails with one of them (or a derived type) goes
    /// straight to <c>Failed</c> and the dead-letter handler runs. Combined with
    /// <see cref="RetryAttribute.IgnoreRetryAttemptExceptions"/> per job type. Empty by default.
    /// </summary>
    public IReadOnlyList<Type> IgnoreRetryAttemptExceptions
    {
        get => _ignoreRetryAttemptExceptions;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _ignoreRetryAttemptExceptions = Internal.ExceptionTypeList.Validate(value, nameof(value))!;
        }
    }

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
    /// Queues are drained in the order specified. Defaults to <c>["default"]</c>, the default queue of the application
    /// (<c>{prefix}.default</c>, see <see cref="QueuePrefix"/>) plus the legacy <c>default</c>.
    /// </summary>
    public IReadOnlyList<string> Queues { get; set; } = ["default"];

    /// <summary>
    /// Prefix applied to the implicit <c>default</c> queue so hosts sharing a database do not share a queue.
    /// When <see langword="null"/> or blank, the lowercase name of the entry assembly is used.
    /// An explicit prefix is stored as typed; only the prefix derived from the assembly name is lowercase. Queues named explicitly, and names that already contain a dot, are never prefixed.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The prefix is longer than 100 characters, contains whitespace, or starts or ends with a dot.
    /// </exception>
    public string? QueuePrefix
    {
        get => _queuePrefix;
        set
        {
            var trimmed = value?.Trim();
            if (!string.IsNullOrEmpty(trimmed)
                && (trimmed.Length > MaxQueuePrefixLength
                    || trimmed.Any(char.IsWhiteSpace)
                    || trimmed.StartsWith('.')
                    || trimmed.EndsWith('.')))
            {
                throw new ArgumentException(
                    $"QueuePrefix must be at most {MaxQueuePrefixLength} characters, without whitespace, and must not start or end with a dot.",
                    nameof(value));
            }

            _queuePrefix = value;
        }
    }

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
    /// How long a job guarded by <see cref="ThrottleAttribute"/> waits for a free slot while it keeps its worker
    /// slot. After this the job is returned to the queue without consuming an attempt, so a saturated resource
    /// cannot occupy every worker. Internal on purpose; tests shorten it.
    /// </summary>
    internal TimeSpan ThrottleMaxWait { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long a job may keep running after its execution timeout cancelled its token before a warning is logged
    /// and counted. Internal on purpose; tests shorten it.
    /// </summary>
    internal TimeSpan CancellationGracePeriod { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The queue names the dispatcher polls: <see cref="Queues"/> mapped to stored names, plus the legacy <c>default</c>.</summary>
    internal IReadOnlyList<string> PolledQueues
    {
        get
        {
            var prefix = EffectivePrefix;
            var result = new List<string>();
            var drainsLegacy = false;
            foreach (var queue in Queues)
            {
                var stored = QueueNames.Resolve(queue, prefix);
                drainsLegacy |= !string.Equals(stored, queue, StringComparison.Ordinal);
                if (!result.Contains(stored, StringComparer.Ordinal))
                {
                    result.Add(stored);
                }
            }

            if (drainsLegacy && !result.Contains(QueueNames.Default, StringComparer.Ordinal))
            {
                result.Add(QueueNames.Default);
            }

            return result;
        }
    }

    /// <summary>The entry assembly the automatic prefix is derived from. Internal on purpose; tests replace it.</summary>
    internal System.Reflection.Assembly? EntryAssembly { get; set; } = System.Reflection.Assembly.GetEntryAssembly();

    /// <summary>The prefix in effect: the explicit <see cref="QueuePrefix"/>, else the entry assembly name.</summary>
    internal string? EffectivePrefix => string.IsNullOrWhiteSpace(QueuePrefix)
        ? QueueNames.DerivePrefix(EntryAssembly)
        : QueuePrefix.Trim();

    /// <summary>
    /// Set by <see cref="ApplySettings"/> when <c>appsettings.json</c> carries a <c>DefaultQueue</c> other than
    /// <c>default</c>: the value is accepted but never applied.
    /// </summary>
    internal string? IgnoredDefaultQueue { get; private set; }

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

        var existing = QueueSettings.Find(q => string.Equals(q.Name, queueName, StringComparison.Ordinal));
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
        IgnoredDefaultQueue = string.Equals(s.DefaultQueue, "default", StringComparison.Ordinal) ? null : s.DefaultQueue;
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
        QueuePrefix = s.QueuePrefix;
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

    /// <summary>
    /// Lists settings that are configured but have no effect, so the dispatcher can say so at startup
    /// instead of ignoring them silently.
    /// </summary>
    /// <returns>The names of the settings that are accepted and not applied.</returns>
    internal IReadOnlyList<string> GetIgnoredSettings()
    {
        var ignored = new List<string>();
        if (IgnoredDefaultQueue is not null)
        {
            ignored.Add("DefaultQueue");
        }

        foreach (var queue in QueueSettings.Where(q => q.Workers.HasValue))
        {
            ignored.Add($"QueueSettings[{queue.Name}].Workers");
        }

        return ignored;
    }

    /// <summary>
    /// Lists the queue names passed to <see cref="ConfigureQueue"/> that differ only by case from a queue this host polls or
    /// from another configured name. Names are matched exactly, so such a setting does not govern the queue it looks like it
    /// was written for; the dispatcher says so at startup instead of letting it fail silently.
    /// </summary>
    /// <returns>One description per mismatched name.</returns>
    internal IReadOnlyList<string> GetQueueNameCaseMismatches()
    {
        _ = QueueSettings;
        return [];
    }

    /// <summary>Maps a user-facing queue name to the name stored with jobs.</summary>
    /// <param name="queue">The queue the caller asked for, or <see langword="null"/> for the implicit default.</param>
    /// <returns>The stored queue name.</returns>
    internal string ResolveQueue(string? queue) => QueueNames.Resolve(queue, EffectivePrefix);

    /// <summary>
    /// Tells whether a stored queue is paused. Pausing <c>default</c> also pauses the prefixed default queue, so
    /// configuration keyed by the old name keeps working.
    /// </summary>
    /// <param name="queue">The stored queue name.</param>
    /// <param name="pausedQueues">The paused queue names.</param>
    /// <returns><see langword="true"/> when the queue is paused.</returns>
    internal bool IsQueuePaused(string queue, HashSet<string> pausedQueues) =>
        pausedQueues.Contains(queue)
        || (string.Equals(queue, ResolveQueue(null), StringComparison.Ordinal) && pausedQueues.Contains(QueueNames.Default));

    /// <summary>
    /// Finds the queue settings (execution window, circuit breaker) that govern a stored queue name. Settings written for
    /// <c>default</c> govern the prefixed default queue and the legacy <c>default</c> that is still drained.
    /// </summary>
    /// <param name="storedQueue">The stored queue name.</param>
    /// <returns>The settings, or <see langword="null"/> when none are configured.</returns>
    internal QueueSettings? SettingsFor(string storedQueue) =>
        QueueSettings.Find(qs => StandsFor(qs.Name, storedQueue));

    /// <summary>
    /// Tells whether a queue name written by the developer stands for a stored queue name: the stored name it resolves to,
    /// and, for the default queue, the legacy <c>default</c> that is still drained.
    /// </summary>
    /// <param name="writtenQueue">The name as written (a <c>TargetQueue</c>, a <c>ConfigureQueue</c> name), or <see langword="null"/> for the default.</param>
    /// <param name="storedQueue">The queue name stored with a job.</param>
    /// <returns><see langword="true"/> when the stored queue is one the written name stands for.</returns>
    internal bool StandsFor(string? writtenQueue, string storedQueue) =>
        string.Equals(ResolveQueue(writtenQueue), storedQueue, StringComparison.Ordinal)
        || (string.Equals(writtenQueue ?? QueueNames.Default, QueueNames.Default, StringComparison.Ordinal)
            && string.Equals(storedQueue, QueueNames.Default, StringComparison.Ordinal));
}
