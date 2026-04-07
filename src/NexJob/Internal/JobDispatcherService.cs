using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Configuration;
using NexJob.Storage;
using NexJob.Telemetry;

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
    private readonly IRuntimeSettingsStore _runtimeStore;
    private readonly NexJobOptions _options;
    private readonly JobWakeUpChannel _wakeUp;
    private readonly ILogger<JobDispatcherService> _logger;
    private int _activeJobCount;

    /// <summary>
    /// Initializes a new <see cref="JobDispatcherService"/>.
    /// </summary>
    public JobDispatcherService(
        IStorageProvider storage,
        IServiceScopeFactory scopeFactory,
        ThrottleRegistry throttleRegistry,
        IRuntimeSettingsStore runtimeStore,
        NexJobOptions options,
        JobWakeUpChannel wakeUp,
        ILogger<JobDispatcherService> logger)
    {
        _storage = storage;
        _scopeFactory = scopeFactory;
        _throttleRegistry = throttleRegistry;
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

            // Filter out paused queues and queues outside their execution window
            RuntimeSettings runtime;
            try
            {
                runtime = await _runtimeStore.GetAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                workerSlots.Release();
                break;
            }

            var activeQueues = GetActiveQueues(runtime);
            if (activeQueues.Count == 0)
            {
                workerSlots.Release();
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
                workerSlots.Release();
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching next job from storage");
                workerSlots.Release();
                await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false);
                continue;
            }

            if (job is null)
            {
                workerSlots.Release();

                var pollingInterval = runtime.PollingInterval ?? _options.PollingInterval;

                await _wakeUp.WaitAsync(pollingInterval, stoppingToken).ConfigureAwait(false);

                continue;
            }

            // Fire-and-forget: slot and active count are released in the finally block inside the task
            Interlocked.Increment(ref _activeJobCount);
            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteJobAsync(job).ConfigureAwait(false);
                }
                finally
                {
                    workerSlots.Release();
                    Interlocked.Decrement(ref _activeJobCount);
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

            // IJob (no-input) — sentinel NoInput, single-parameter ExecuteAsync
            if (it == typeof(NoInput))
            {
                var noInputMethod = jt.GetMethod(
                    nameof(IJob.ExecuteAsync),
                    [typeof(CancellationToken)])!;

                var jobParam = Expression.Parameter(typeof(object), "job");
                var inputParam = Expression.Parameter(typeof(object), "input"); // ignored
                var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");

                var call = Expression.Call(
                    Expression.Convert(jobParam, jt),
                    noInputMethod,
                    ctParam);

                return Expression.Lambda<Func<object, object, CancellationToken, Task>>(
                    call, jobParam, inputParam, ctParam).Compile();
            }

            // IJob<TInput> — original path
            var method = jt.GetMethod(nameof(IJob<object>.ExecuteAsync),
                [it, typeof(CancellationToken)])!;

            var jp = Expression.Parameter(typeof(object), "job");
            var ip = Expression.Parameter(typeof(object), "input");
            var ct = Expression.Parameter(typeof(CancellationToken), "ct");

            var callTyped = Expression.Call(
                Expression.Convert(jp, jt),
                method,
                Expression.Convert(ip, it),
                ct);

            return Expression.Lambda<Func<object, object, CancellationToken, Task>>(
                callTyped, jp, ip, ct).Compile();
        });
    }

    // ─── execution pipeline ──────────────────────────────────────────────────

    /// <summary>
    /// Records success metrics (duration and job count) for the completed job.
    /// </summary>
    private static void RecordSuccessMetrics(string jobType, TimeSpan elapsed)
    {
        NexJobMetrics.JobDuration.Record(elapsed.TotalMilliseconds,
            new TagList { { "nexjob.job_type", jobType }, { "nexjob.status", "succeeded" } });
        NexJobMetrics.JobsSucceeded.Add(1,
            new TagList { { "nexjob.job_type", jobType } });
    }

    private async Task ExecuteJobAsync(JobRecord job)
    {
        if (await TryHandleExpirationAsync(job).ConfigureAwait(false))
        {
            return;
        }

        _logger.LogDebug("Executing job {JobId} ({JobType}), attempt {Attempt}/{Max}",
            job.Id, job.JobType, job.Attempts, job.MaxAttempts);

        using var cts = new CancellationTokenSource();
        var heartbeatTask = RunHeartbeatAsync(job.Id, cts.Token);

        using var logScope = new JobExecutionLogScope(_options.MaxJobLogLines);
        using var activity = NexJobActivitySource.StartExecute(job.JobType, job.Queue, job.TraceParent);
        activity?.SetTag("nexjob.job_id", job.Id.Value.ToString());
        activity?.SetTag("nexjob.attempt", job.Attempts);
        var sw = Stopwatch.StartNew();

        try
        {
            using var context = await PrepareInvocationAsync(job).ConfigureAwait(false);
            await ExecuteWithThrottlingAsync(context, cts.Token).ConfigureAwait(false);

            sw.Stop();
            RecordSuccessMetrics(job.JobType, sw.Elapsed);
            activity?.SetStatus(ActivityStatusCode.Ok);

            await _storage.AcknowledgeAsync(job.Id, CancellationToken.None).ConfigureAwait(false);
            await _storage.SaveExecutionLogsAsync(job.Id, logScope.Entries, CancellationToken.None).ConfigureAwait(false);
            await _storage.EnqueueContinuationsAsync(job.Id, CancellationToken.None).ConfigureAwait(false);

            if (job.RecurringJobId is not null)
            {
                await _storage.SetRecurringJobLastExecutionResultAsync(
                    job.RecurringJobId, JobStatus.Succeeded, null, CancellationToken.None).ConfigureAwait(false);
            }

            _logger.LogDebug("Job {JobId} completed successfully", job.Id);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var retryAt = await HandleFailureAsync(job, ex, activity).ConfigureAwait(false);

            await _storage.SetFailedAsync(job.Id, ex, retryAt, CancellationToken.None).ConfigureAwait(false);
            await _storage.SaveExecutionLogsAsync(job.Id, logScope.Entries, CancellationToken.None).ConfigureAwait(false);

            if (job.RecurringJobId is not null && retryAt is null)
            {
                await _storage.SetRecurringJobLastExecutionResultAsync(
                    job.RecurringJobId, JobStatus.Failed, ex.Message, CancellationToken.None).ConfigureAwait(false);
            }

            if (retryAt is null)
            {
                await InvokeDeadLetterHandlerAsync(job, ex, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            await cts.CancelAsync().ConfigureAwait(false);
            await heartbeatTask.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Checks if the job has passed its deadline and marks it as expired if so.
    /// Returns <see langword="true"/> if the job was expired and execution should stop.
    /// </summary>
    private async Task<bool> TryHandleExpirationAsync(JobRecord job)
    {
        if (!job.ExpiresAt.HasValue || DateTimeOffset.UtcNow <= job.ExpiresAt.Value)
        {
            return false;
        }

        _logger.LogInformation(
            "Job {JobId} ({JobType}) expired at {ExpiresAt} — discarding",
            job.Id, job.JobType, job.ExpiresAt.Value);

        await _storage.SetExpiredAsync(job.Id, CancellationToken.None).ConfigureAwait(false);

        NexJobMetrics.JobsExpired.Add(1, new TagList { { "nexjob.job_type", job.JobType } });

        return true;
    }

    /// <summary>
    /// Resolves the job type, input type, applies schema migration, deserializes input,
    /// and resolves the job instance from DI. Returns an invocation context ready for execution.
    /// </summary>
    private async Task<JobInvocationContext> PrepareInvocationAsync(JobRecord job)
    {
        var scope = _scopeFactory.CreateScope();

        scope.ServiceProvider.GetRequiredService<IJobContextAccessor>().Context =
            new JobContext(job, _storage);

        var jobType = JobTypeResolver.ResolveJobType(job.JobType)
                      ?? throw new InvalidOperationException($"Cannot load job type: {job.JobType}");
        var inputType = JobTypeResolver.ResolveInputType(job.InputType)
                       ?? throw new InvalidOperationException($"Cannot load input type: {job.InputType}");

        var currentVersion = jobType.GetCustomAttribute<SchemaVersionAttribute>()?.Version ?? 1;
        var migratedJson = scope.ServiceProvider
            .GetRequiredService<MigrationPipeline>()
            .Migrate(job.InputJson, job.SchemaVersion, currentVersion, inputType);

        var input = JsonSerializer.Deserialize(migratedJson, inputType)
                    ?? throw new InvalidOperationException($"Deserialized null input for job {job.Id}.");

        var jobInstance = scope.ServiceProvider.GetRequiredService(jobType);
        var invoker = GetOrBuildInvoker(jobType, inputType);
        var throttleAttrs = jobType.GetCustomAttributes<ThrottleAttribute>(inherit: true);

        return await Task.FromResult(new JobInvocationContext(scope, jobInstance, input, invoker, throttleAttrs)).ConfigureAwait(false);
    }

    /// <summary>
    /// Acquires all throttle semaphores declared on the job type, invokes the job,
    /// then releases the semaphores. Throttle semaphores are always released in the finally block.
    /// </summary>
    private async Task ExecuteWithThrottlingAsync(JobInvocationContext ctx, CancellationToken cancellationToken)
    {
        var acquired = new List<SemaphoreSlim>();

        foreach (var attr in ctx.ThrottleAttributes)
        {
            var sem = _throttleRegistry.GetOrCreate(attr.Resource, attr.MaxConcurrent);
            _logger.LogDebug("Job waiting for throttle slot on resource '{Resource}' (max={Max})",
                attr.Resource, attr.MaxConcurrent);
            await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired.Add(sem);
        }

        try
        {
            await ctx.Invoker(ctx.JobInstance, ctx.Input, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            foreach (var sem in acquired)
            {
                sem.Release();
            }
        }
    }

    /// <summary>
    /// Records failure metrics, logs the failure decision (retry or dead-letter),
    /// and returns the scheduled retry time if a retry should occur, or null for dead-letter.
    /// </summary>
    private async Task<DateTimeOffset?> HandleFailureAsync(JobRecord job, Exception ex, Activity? activity)
    {
        _logger.LogWarning(ex, "Job {JobId} failed on attempt {Attempt}", job.Id, job.Attempts);

        NexJobMetrics.JobsFailed.Add(1, new TagList { { "nexjob.job_type", job.JobType } });
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            { "exception.type", ex.GetType().FullName },
            { "exception.message", ex.Message },
            { "exception.stacktrace", ex.StackTrace },
        }));

        var retryAttr = job.JobType is not null
            ? Type.GetType(job.JobType)?.GetCustomAttribute<RetryAttribute>(inherit: true)
            : null;
        var effectiveMaxAttempts = retryAttr?.Attempts ?? job.MaxAttempts;

        DateTimeOffset? retryAt = null;

        if (job.Attempts < effectiveMaxAttempts)
        {
            var retryDelay = retryAttr?.InitialDelay is not null
                ? retryAttr.ComputeDelay(job.Attempts)
                : _options.RetryDelayFactory(job.Attempts);

            retryAt = DateTimeOffset.UtcNow + retryDelay;

            _logger.LogInformation(
                "Job {JobId} scheduled for retry {Attempt}/{Max} at {RetryAt} (delay: {Delay}s)",
                job.Id, job.Attempts + 1, effectiveMaxAttempts, retryAt.Value, retryDelay.TotalSeconds);
        }
        else
        {
            _logger.LogError(ex,
                "Job {JobId} exhausted all {Max} attempts — moving to dead-letter",
                job.Id, effectiveMaxAttempts);
        }

        return await Task.FromResult(retryAt).ConfigureAwait(false);
    }

    // ─── dead-letter handling ─────────────────────────────────────────────────

    private async Task InvokeDeadLetterHandlerAsync(
        JobRecord job,
        Exception lastException,
        CancellationToken cancellationToken)
    {
        try
        {
            var jobType = JobTypeResolver.ResolveJobType(job.JobType);
            if (jobType is null)
            {
                _logger.LogDebug(
                    "Cannot resolve job type {JobType} for dead-letter handler — handler skipped",
                    job.JobType);
                return;
            }

            using var scope = _scopeFactory.CreateScope();

            var handlerType = typeof(IDeadLetterHandler<>).MakeGenericType(jobType);
            var handler = scope.ServiceProvider.GetService(handlerType);
            if (handler is null)
            {
                return;
            }

            var method = handlerType.GetMethod(nameof(IDeadLetterHandler<object>.HandleAsync))
                        ?? throw new InvalidOperationException($"Cannot find HandleAsync method on {handlerType.Name}");

            var task = (Task)method.Invoke(handler, new object[] { job, lastException, cancellationToken })!;
            await task.ConfigureAwait(false);

            _logger.LogDebug(
                "Dead-letter handler {Handler} invoked for job {JobId}",
                handlerType.Name, job.Id);
        }
        catch (Exception handlerEx)
        {
            _logger.LogError(
                handlerEx,
                "Dead-letter handler threw for job {JobId} — handler errors are swallowed",
                job.Id);
        }
    }

    // ─── queue filtering ──────────────────────────────────────────────────────

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

    private async Task RunHeartbeatAsync(JobId jobId, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_options.HeartbeatInterval, cancellationToken).ConfigureAwait(false);
                await _storage.UpdateHeartbeatAsync(jobId, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on job completion
        }
    }

    // ─── nested types ─────────────────────────────────────────────────────────

    /// <summary>
    /// Captures all state needed to invoke a job: DI scope, job instance, deserialized input,
    /// compiled invoker, and throttle attributes. Implements IDisposable to dispose the scope.
    /// </summary>
    private sealed record JobInvocationContext(
        IServiceScope Scope,
        object JobInstance,
        object Input,
        Func<object, object, CancellationToken, Task> Invoker,
        IEnumerable<ThrottleAttribute> ThrottleAttributes) : IDisposable
    {
        public void Dispose() => Scope.Dispose();
    }
}
