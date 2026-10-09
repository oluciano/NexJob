using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Configuration;
using NexJob.Storage;
using NexJob.Telemetry;

namespace NexJob.Internal;

/// <summary>
/// Hosted background service that continuously polls the storage provider for
/// ready jobs and dispatches them to a bounded worker pool via <see cref="IJobExecutor"/>.
/// </summary>
internal sealed class JobDispatcherService : BackgroundService
{
    private readonly IJobStorage _storage;
    private readonly IJobExecutor _executor;
    private readonly IRuntimeSettingsStore _runtimeStore;
    private readonly NexJobOptions _options;
    private readonly JobWakeUpChannel _wakeUp;
    private readonly IQueueCircuitBreakerManager? _circuitBreakerManager;
    private readonly ILogger<JobDispatcherService> _logger;
    private IReadOnlyList<string> _polledQueues = [];
    private readonly CancellationTokenSource _stopFetching = new();
    private SemaphoreSlim? _workerSlots;
    private int _activeJobCount;

    /// <summary>
    /// Initializes a new <see cref="JobDispatcherService"/>.
    /// </summary>
    public JobDispatcherService(
        IJobStorage storage,
        IJobExecutor executor,
        IRuntimeSettingsStore runtimeStore,
        NexJobOptions options,
        JobWakeUpChannel wakeUp,
        ILogger<JobDispatcherService> logger,
        IQueueCircuitBreakerManager? circuitBreakerManager = null)
    {
        _storage = storage;
        _executor = executor;
        _runtimeStore = runtimeStore;
        _options = options;
        _wakeUp = wakeUp;
        _logger = logger;
        _circuitBreakerManager = circuitBreakerManager;
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop pulling new work first; running jobs keep the host stoppingToken and are only cancelled after the drain.
        await _stopFetching.CancelAsync().ConfigureAwait(false);

        _logger.LogInformation(
            "NexJob shutting down. Waiting for {Count} active job(s) to complete (timeout: {Timeout}s)...",
            _activeJobCount, _options.ShutdownTimeout.TotalSeconds);

        using var timeoutCts = new CancellationTokenSource(_options.ShutdownTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        while (_activeJobCount > 0 && !linkedCts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(250, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (_activeJobCount > 0)
        {
            _logger.LogWarning(
                "Shutdown timeout reached. {Count} job(s) still active — they will be requeued by the orphan watcher.",
                _activeJobCount);
        }
        else
        {
            _logger.LogInformation("All active jobs completed cleanly.");
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        _stopFetching.Dispose();
        _workerSlots?.Dispose();
        base.Dispose();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _polledQueues = _options.PolledQueues;
        _logger.LogInformation("JobDispatcherService started. Workers: {Workers}, Queues: {Queues}",
            _options.Workers, string.Join(", ", _polledQueues));

        if (string.IsNullOrWhiteSpace(_options.QueuePrefix) && _options.EffectivePrefix is { } derivedPrefix)
        {
            _logger.LogWarning(
                "The default queue prefix '{Prefix}' was derived from the entry assembly name. Renaming the assembly changes it and strands jobs in the old queue; set NexJobOptions.QueuePrefix explicitly in production.",
                derivedPrefix);
        }

        if (string.IsNullOrWhiteSpace(_options.QueuePrefix) && _options.EffectivePrefix is null)
        {
            _logger.LogWarning(
                "No default queue prefix: there is no entry assembly to derive it from and NexJobOptions.QueuePrefix is not set, so jobs enqueued without a queue go to the shared 'default' queue. Set NexJobOptions.QueuePrefix if other applications use this database.");
        }

        var ignoredSettings = _options.GetIgnoredSettings();
        if (ignoredSettings.Count > 0)
        {
            var hint = ignoredSettings.Contains("DefaultQueue", StringComparer.Ordinal)
                ? " DefaultQueue is not used: to name the default queue of this application set NexJobOptions.QueuePrefix."
                : string.Empty;
            _logger.LogWarning(
                "These settings are configured but have no effect and are ignored: {Settings}. The worker count is a deployment setting (NexJobOptions.Workers).{Hint}",
                string.Join(", ", ignoredSettings),
                hint);
        }

        // Outlives ExecuteAsync: jobs still draining during shutdown release their slot after the loop exits.
        var workerSlots = new SemaphoreSlim(_options.Workers, _options.Workers);
        _workerSlots = workerSlots;
        NexJobMetrics.SetWorkerMetricsProviders(
            () => Volatile.Read(ref _activeJobCount),
            () => _options.Workers);

        // Fetching stops when the host stops or as soon as StopAsync begins. Running jobs keep
        // the host stopping token so they are only cancelled after the drain timeout.
        using var fetchCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _stopFetching.Token);
        var fetchToken = fetchCts.Token;

        try
        {
            while (!fetchToken.IsCancellationRequested)
            {
                await workerSlots.WaitAsync(fetchToken).ConfigureAwait(false);

                // Determine how many extra idle workers we can fill right now in a single batch
                var availableSlots = 1 + workerSlots.CurrentCount;

                var slotsAcquired = 1;
                try
                {
                    // Filter out paused queues and queues outside their execution window
                    RuntimeSettings runtime;
                    try
                    {
                        runtime = await _runtimeStore.GetAsync(fetchToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    var activeQueues = GetActiveQueues(runtime);
                    if (activeQueues.Count == 0)
                    {
                        var delay = runtime.PollingInterval ?? _options.PollingInterval;
                        _logger.LogDebug(
                            "No active queues at this time — all queues are paused or outside their execution window. Next poll in {Delay}ms",
                            delay.TotalMilliseconds);
                        await Task.Delay(delay, fetchToken).ConfigureAwait(false);
                        continue;
                    }

                    IReadOnlyList<JobRecord> jobs;
                    try
                    {
                        jobs = await _storage.FetchBatchAsync(activeQueues, availableSlots, fetchToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error fetching next jobs from storage");
                        await Task.Delay(_options.PollingInterval, fetchToken).ConfigureAwait(false);
                        continue;
                    }

                    if (jobs.Count == 0)
                    {
                        var pollingInterval = runtime.PollingInterval ?? _options.PollingInterval;
                        await _wakeUp.WaitAsync(pollingInterval, fetchToken).ConfigureAwait(false);
                        continue;
                    }

                    // Acquire the additional semaphore slots for the jobs actually retrieved (beyond the first one already acquired)
                    for (var i = 1; i < jobs.Count; i++)
                    {
                        if (await workerSlots.WaitAsync(TimeSpan.Zero, fetchToken).ConfigureAwait(false))
                        {
                            slotsAcquired++;
                        }
                    }

                    // Dispatch all fetched jobs concurrently to worker pool
                    for (var i = 0; i < jobs.Count; i++)
                    {
                        var job = jobs[i];
                        Interlocked.Increment(ref _activeJobCount);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _executor.ExecuteJobAsync(job, stoppingToken).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                // Unhandled storage or pipeline errors must not become unobserved task exceptions.
                                _logger.LogError(ex, "Unhandled error while executing job {JobId}", job.Id);
                            }
                            finally
                            {
                                workerSlots.Release();
                                Interlocked.Decrement(ref _activeJobCount);
                            }
                        }, CancellationToken.None);
                    }

                    // If we acquired more semaphore slots than actual jobs returned, release the excess
                    if (slotsAcquired > jobs.Count)
                    {
                        workerSlots.Release(slotsAcquired - jobs.Count);
                    }

                    // Since we successfully handed off the jobs to background worker tasks with their own release(),
                    // we set slotsAcquired = 0 so finally doesn't release them again.
                    slotsAcquired = 0;
                }
                finally
                {
                    if (slotsAcquired > 0)
                    {
                        workerSlots.Release(slotsAcquired);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (fetchToken.IsCancellationRequested)
        {
            // Shutdown began while waiting for a slot, a poll or a fetch — leave the loop cleanly.
        }

        NexJobMetrics.SetWorkerMetricsProviders(null, null);
        _logger.LogInformation("JobDispatcherService stopped.");
    }

    private IReadOnlyList<string> GetActiveQueues(RuntimeSettings runtime)
    {
        var now = DateTimeOffset.UtcNow;
        var result = new List<string>();

        foreach (var q in _polledQueues)
        {
            if (_options.IsQueuePaused(q, runtime.PausedQueues))
            {
                _logger.LogDebug("Queue '{Queue}' skipped — paused", q);
                continue;
            }

            var settings = _options.QueueSettings.Find(qs => string.Equals(_options.ResolveQueue(qs.Name), q, StringComparison.Ordinal));
            if (settings?.ExecutionWindow is not null && !settings.ExecutionWindow.IsWithinWindow(now))
            {
                _logger.LogDebug("Queue '{Queue}' skipped — outside execution window", q);
                continue;
            }

            if (_circuitBreakerManager is not null)
            {
                var state = _circuitBreakerManager.GetState(q, out var allowed);
                if (state == QueueCircuitState.Open || allowed <= 0)
                {
                    _logger.LogDebug("Queue '{Queue}' skipped — circuit breaker is OPEN", q);
                    continue;
                }
            }

            result.Add(q);
        }

        return result;
    }
}
