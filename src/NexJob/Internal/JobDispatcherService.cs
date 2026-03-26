using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Storage;

namespace NexJob.Internal;

/// <summary>
/// Hosted background service that continuously polls the storage provider for
/// ready jobs and dispatches them to a bounded worker pool.
/// Each job execution receives a fresh DI scope, enforcing proper lifetime isolation.
/// </summary>
internal sealed class JobDispatcherService : BackgroundService
{
    // Compiled invoker cache: avoids repeated reflection on hot paths
    private static readonly ConcurrentDictionary<(Type Job, Type Input), Func<object, object, CancellationToken, Task>>
        InvokerCache = new();

    private readonly IStorageProvider _storage;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ThrottleRegistry _throttleRegistry;
    private readonly NexJobOptions _options;
    private readonly ILogger<JobDispatcherService> _logger;

    /// <summary>
    /// Initializes a new <see cref="JobDispatcherService"/>.
    /// </summary>
    public JobDispatcherService(
        IStorageProvider storage,
        IServiceScopeFactory scopeFactory,
        ThrottleRegistry throttleRegistry,
        NexJobOptions options,
        ILogger<JobDispatcherService> logger)
    {
        _storage = storage;
        _scopeFactory = scopeFactory;
        _throttleRegistry = throttleRegistry;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JobDispatcherService started. Workers: {Workers}, Queues: {Queues}",
            _options.Workers, string.Join(", ", _options.Queues));

        using var workerSlots = new SemaphoreSlim(_options.Workers, _options.Workers);

        while (!stoppingToken.IsCancellationRequested)
        {
            await workerSlots.WaitAsync(stoppingToken);

            JobRecord? job;
            try
            {
                job = await _storage.FetchNextAsync(_options.Queues, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                workerSlots.Release();
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching next job from storage");
                workerSlots.Release();
                await Task.Delay(_options.PollingInterval, stoppingToken);
                continue;
            }

            if (job is null)
            {
                workerSlots.Release();
                // FetchNextAsync already introduced a small delay; add the configured polling
                // interval only when we know the queues are truly empty
                continue;
            }

            // Fire-and-forget: slot is released in the finally block inside the task
            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteJobAsync(job, stoppingToken);
                }
                finally
                {
                    workerSlots.Release();
                }
            }, CancellationToken.None);
        }

        _logger.LogInformation("JobDispatcherService stopped.");
    }

    // ─── invoker cache ───────────────────────────────────────────────────────

    private static Func<object, object, CancellationToken, Task> GetOrBuildInvoker(
        Type jobType, Type inputType)
    {
        return InvokerCache.GetOrAdd((jobType, inputType), static key =>
        {
            var (jt, it) = key;
            var method = jt.GetMethod(nameof(IJob<object>.ExecuteAsync),
                [it, typeof(CancellationToken)])!;

            var jobParam = Expression.Parameter(typeof(object), "job");
            var inputParam = Expression.Parameter(typeof(object), "input");
            var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

            var call = Expression.Call(
                Expression.Convert(jobParam, jt),
                method,
                Expression.Convert(inputParam, it),
                ctParam);

            return Expression.Lambda<Func<object, object, CancellationToken, Task>>(
                call, jobParam, inputParam, ctParam).Compile();
        });
    }

    // ─── execution pipeline ──────────────────────────────────────────────────

    private async Task ExecuteJobAsync(JobRecord job, CancellationToken stoppingToken)
    {
        _logger.LogDebug("Executing job {JobId} ({JobType}), attempt {Attempt}/{Max}",
            job.Id, job.JobType, job.Attempts, job.MaxAttempts);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeatTask = RunHeartbeatAsync(job.Id, cts.Token);

        // Create capture provider before execution; register it with the app's ILoggerFactory.
        // Note: AddProvider affects the singleton ILoggerFactory, so all log categories emitted
        // during this job's execution window will be captured — not only the job's own logger.
        var captureProvider = new JobCaptureLoggerProvider(_options.MaxJobLogLines);

        try
        {
            using var scope = _scopeFactory.CreateScope();

            var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
            loggerFactory.AddProvider(captureProvider);

            var jobType = Type.GetType(job.JobType, throwOnError: true)!;
            var inputType = Type.GetType(job.InputType, throwOnError: true)!;
            var input = JsonSerializer.Deserialize(job.InputJson, inputType)
                        ?? throw new InvalidOperationException($"Deserialized null input for job {job.Id}.");

            var jobInstance = scope.ServiceProvider.GetRequiredService(jobType);
            var invoker = GetOrBuildInvoker(jobType, inputType);

            // Enforce [Throttle] if present
            var throttleAttrs = jobType.GetCustomAttributes<ThrottleAttribute>(inherit: true);
            var acquired = new List<SemaphoreSlim>();

            foreach (var attr in throttleAttrs)
            {
                var sem = _throttleRegistry.GetOrCreate(attr.Resource, attr.MaxConcurrent);
                await sem.WaitAsync(stoppingToken);
                acquired.Add(sem);
            }

            try
            {
                await invoker(jobInstance, input, stoppingToken);
            }
            finally
            {
                foreach (var sem in acquired)
                {
                    sem.Release();
                }
            }

            await _storage.AcknowledgeAsync(job.Id, CancellationToken.None);
            await _storage.SaveExecutionLogsAsync(job.Id, captureProvider.Entries, CancellationToken.None);
            await _storage.EnqueueContinuationsAsync(job.Id, CancellationToken.None);

            if (job.RecurringJobId is not null)
            {
                await _storage.SetRecurringJobLastExecutionResultAsync(
                    job.RecurringJobId, JobStatus.Succeeded, null, CancellationToken.None);
            }

            _logger.LogDebug("Job {JobId} completed successfully", job.Id);
        }
        catch (OperationCanceledException ex) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down — do not retry; the orphan watcher will requeue
            _logger.LogWarning(ex, "Job {JobId} was interrupted by host shutdown", job.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job {JobId} failed on attempt {Attempt}", job.Id, job.Attempts);

            DateTimeOffset? retryAt = null;

            if (job.Attempts < job.MaxAttempts)
            {
                retryAt = DateTimeOffset.UtcNow + _options.RetryDelayFactory(job.Attempts);
            }
            else
            {
                _logger.LogError(ex, "Job {JobId} exhausted all {Max} attempts — moving to dead-letter",
                    job.Id, job.MaxAttempts);
            }

            await _storage.SetFailedAsync(job.Id, ex, retryAt, CancellationToken.None);
            await _storage.SaveExecutionLogsAsync(job.Id, captureProvider.Entries, CancellationToken.None);

            if (job.RecurringJobId is not null && retryAt is null)
            {
                await _storage.SetRecurringJobLastExecutionResultAsync(
                    job.RecurringJobId, JobStatus.Failed, ex.Message, CancellationToken.None);
            }
        }
        finally
        {
            await cts.CancelAsync();
            await heartbeatTask;
        }
    }

    private async Task RunHeartbeatAsync(JobId jobId, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_options.HeartbeatInterval, cancellationToken);
                await _storage.UpdateHeartbeatAsync(jobId, CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on job completion
        }
    }
}
