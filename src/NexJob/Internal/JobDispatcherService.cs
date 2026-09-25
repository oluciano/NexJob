using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Configuration;
using NexJob.Storage;
using NexJob.Telemetry;

namespace NexJob.Internal;

/// <summary>
/// Hosted background service that continuously polls the storage provider for
/// ready jobs and dispatches them to a bounded worker pool via <see cref="JobExecutor"/>.
/// </summary>
internal sealed class JobDispatcherService : BackgroundService
{
    private readonly IJobStorage _storage;
    private readonly JobExecutor _executor;
    private readonly IRuntimeSettingsStore _runtimeStore;
    private readonly NexJobOptions _options;
    private readonly JobWakeUpChannel _wakeUp;
    private readonly ILogger<JobDispatcherService> _logger;
    private int _activeJobCount;

    /// <summary>
    /// Initializes a new <see cref="JobDispatcherService"/>.
    /// </summary>
    public JobDispatcherService(
        IJobStorage storage,
        JobExecutor executor,
        IRuntimeSettingsStore runtimeStore,
        NexJobOptions options,
        JobWakeUpChannel wakeUp,
        ILogger<JobDispatcherService> logger)
    {
        _storage = storage;
        _executor = executor;
        _runtimeStore = runtimeStore;
        _options = options;
        _wakeUp = wakeUp;
        _logger = logger;
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
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
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JobDispatcherService started. Workers: {Workers}, Queues: {Queues}",
            _options.Workers, string.Join(", ", _options.Queues));

        using var workerSlots = new SemaphoreSlim(_options.Workers, _options.Workers);
        NexJobMetrics.SetWorkerMetricsProviders(
            () => Volatile.Read(ref _activeJobCount),
            () => _options.Workers);

        while (!stoppingToken.IsCancellationRequested)
        {
            await workerSlots.WaitAsync(stoppingToken).ConfigureAwait(false);

            // Determine how many extra idle workers we can fill right now in a single batch
            var availableSlots = 1 + workerSlots.CurrentCount;

            var slotsAcquired = 1;
            try
            {
                // Filter out paused queues and queues outside their execution window
                RuntimeSettings runtime;
                try
                {
                    runtime = await _runtimeStore.GetAsync(stoppingToken).ConfigureAwait(false);
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
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                IReadOnlyList<JobRecord> jobs;
                try
                {
                    jobs = await _storage.FetchBatchAsync(activeQueues, availableSlots, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching next jobs from storage");
                    await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (jobs.Count == 0)
                {
                    var pollingInterval = runtime.PollingInterval ?? _options.PollingInterval;
                    await _wakeUp.WaitAsync(pollingInterval, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // Acquire the additional semaphore slots for the jobs actually retrieved (beyond the first one already acquired)
                for (var i = 1; i < jobs.Count; i++)
                {
                    if (await workerSlots.WaitAsync(TimeSpan.Zero, stoppingToken).ConfigureAwait(false))
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

        NexJobMetrics.SetWorkerMetricsProviders(null, null);
        _logger.LogInformation("JobDispatcherService stopped.");
    }

    private IReadOnlyList<string> GetActiveQueues(RuntimeSettings runtime)
    {
        var now = DateTimeOffset.UtcNow;
        var result = new List<string>();

        foreach (var q in _options.Queues)
        {
            if (runtime.PausedQueues.Contains(q))
            {
                _logger.LogDebug("Queue '{Queue}' skipped — paused", q);
                continue;
            }

            var settings = _options.QueueSettings.Find(qs => string.Equals(qs.Name, q, StringComparison.Ordinal));
            if (settings?.ExecutionWindow is not null && !settings.ExecutionWindow.IsWithinWindow(now))
            {
                _logger.LogDebug("Queue '{Queue}' skipped — outside execution window", q);
                continue;
            }

            result.Add(q);
        }

        return result;
    }
}
