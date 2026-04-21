using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Configuration;
using NexJob.Storage;

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

        var deadline = Task.Delay(_options.ShutdownTimeout, CancellationToken.None);

        while (_activeJobCount > 0 && !deadline.IsCompleted)
        {
            await Task.Delay(250, CancellationToken.None).ConfigureAwait(false);
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

        while (!stoppingToken.IsCancellationRequested)
        {
            await workerSlots.WaitAsync(stoppingToken).ConfigureAwait(false);

            var slotTransferred = false;
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

                JobRecord? job;
                try
                {
                    job = await _storage.FetchNextAsync(activeQueues, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching next job from storage");
                    await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (job is null)
                {
                    var pollingInterval = runtime.PollingInterval ?? _options.PollingInterval;
                    await _wakeUp.WaitAsync(pollingInterval, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                // Transfer slot ownership to the worker task
                slotTransferred = true;
                Interlocked.Increment(ref _activeJobCount);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _executor.ExecuteJobAsync(job).ConfigureAwait(false);
                    }
                    finally
                    {
                        workerSlots.Release();
                        Interlocked.Decrement(ref _activeJobCount);
                    }
                }, CancellationToken.None);
            }
            finally
            {
                if (!slotTransferred)
                {
                    workerSlots.Release();
                }
            }
        }

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
