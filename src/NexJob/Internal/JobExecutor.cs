using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NexJob.Storage;
using NexJob.Telemetry;

namespace NexJob.Internal;

/// <summary>
/// Orchestrates the execution of a single NexJob job, including deadline enforcement,
/// DI scope management, input deserialization, throttling, and failure handling.
/// </summary>
internal sealed class JobExecutor
{
    private readonly IJobStorage _storage;
    private readonly IJobInvokerFactory _invokerFactory;
    private readonly IJobRetryPolicy _retryPolicy;
    private readonly IDeadLetterDispatcher _deadLetterDispatcher;
    private readonly ThrottleRegistry _throttleRegistry;
    private readonly NexJobOptions _options;
    private readonly ILogger<JobExecutor> _logger;
    private readonly IReadOnlyList<IJobExecutionFilter> _filters;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobExecutor"/> class.
    /// </summary>
    /// <param name="storage">The job storage.</param>
    /// <param name="invokerFactory">The job invoker factory.</param>
    /// <param name="retryPolicy">The job retry policy.</param>
    /// <param name="deadLetterDispatcher">The dead-letter dispatcher.</param>
    /// <param name="throttleRegistry">The throttle registry.</param>
    /// <param name="options">The nex job options.</param>
    /// <param name="filters">The filters.</param>
    /// <param name="logger">The logger.</param>
    public JobExecutor(
        IJobStorage storage,
        IJobInvokerFactory invokerFactory,
        IJobRetryPolicy retryPolicy,
        IDeadLetterDispatcher deadLetterDispatcher,
        ThrottleRegistry throttleRegistry,
        NexJobOptions options,
        IEnumerable<IJobExecutionFilter> filters,
        ILogger<JobExecutor> logger)
    {
        _storage = storage;
        _invokerFactory = invokerFactory;
        _retryPolicy = retryPolicy;
        _deadLetterDispatcher = deadLetterDispatcher;
        _throttleRegistry = throttleRegistry;
        _options = options;
        _logger = logger;
        _filters = filters.ToList().AsReadOnly();
    }

    /// <summary>
    /// Executes the job asynchronously.
    /// </summary>
    /// <param name="job">The job.</param>
    /// <returns>A task.</returns>
    public async Task ExecuteJobAsync(JobRecord job)
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
            using var context = await _invokerFactory.PrepareAsync(job, cts.Token).ConfigureAwait(false);
            await ExecuteWithThrottlingAndFiltersAsync(context, job, cts.Token).ConfigureAwait(false);

            sw.Stop();
            RecordSuccessMetrics(job.JobType, sw.Elapsed);
            activity?.SetStatus(ActivityStatusCode.Ok);

            await _storage.CommitJobResultAsync(job.Id, new JobExecutionResult
            {
                Succeeded = true,
                Logs = logScope.Entries,
                RecurringJobId = job.RecurringJobId,
            }, CancellationToken.None).ConfigureAwait(false);

            _logger.LogDebug("Job {JobId} completed successfully", job.Id);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var retryAt = await HandleFailureAsync(job, ex, activity).ConfigureAwait(false);

            await _storage.CommitJobResultAsync(job.Id, new JobExecutionResult
            {
                Succeeded = false,
                Logs = logScope.Entries,
                Exception = ex,
                RetryAt = retryAt,
                RecurringJobId = job.RecurringJobId,
            }, CancellationToken.None).ConfigureAwait(false);

            if (retryAt is null)
            {
                await _deadLetterDispatcher.DispatchAsync(job, ex, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            await cts.CancelAsync().ConfigureAwait(false);
            await heartbeatTask.ConfigureAwait(false);
        }
    }

    private static void RecordSuccessMetrics(string jobType, TimeSpan elapsed)
    {
        NexJobMetrics.JobDuration.Record(elapsed.TotalMilliseconds,
            new TagList { { "nexjob.job_type", jobType }, { "nexjob.status", "succeeded" } });
        NexJobMetrics.JobsSucceeded.Add(1,
            new TagList { { "nexjob.job_type", jobType } });
    }

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

    private async Task ExecuteWithThrottlingAndFiltersAsync(
        JobInvocationContext ctx,
        JobRecord job,
        CancellationToken cancellationToken)
    {
        var acquired = new List<ThrottleAttribute>();

        foreach (var attr in ctx.ThrottleAttributes)
        {
            _logger.LogDebug("Job waiting for throttle slot on resource '{Resource}' (max={Max})",
                attr.Resource, attr.MaxConcurrent);

            while (!await _throttleRegistry.TryAcquireWithWaitAsync(
                attr.Resource,
                attr.MaxConcurrent,
                TimeSpan.FromMilliseconds(500),
                cancellationToken).ConfigureAwait(false))
            {
                // Slot ainda ocupado — yield para não monopolizar o worker
                await Task.Yield();
            }

            acquired.Add(attr);
        }

        try
        {
            // Terminal delegate: invokes the actual job
            JobExecutionDelegate jobInvoker = ct =>
                ctx.Invoker(ctx.JobInstance, ctx.Input, ct);

            if (_filters.Count == 0)
            {
                // Fast path: no filters registered
                await jobInvoker(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var context = new JobExecutingContext(job, ctx.Scope.ServiceProvider);

                var pipeline = JobFilterPipeline.Build(_filters, context, jobInvoker);

                try
                {
                    await pipeline(cancellationToken).ConfigureAwait(false);
                    context.Succeeded = true;
                }
                catch (Exception ex)
                {
                    context.Exception = ex;
                    context.Succeeded = false;
                    throw; // re-throw so dispatcher handles retry/dead-letter normally
                }
            }
        }
        finally
        {
            foreach (var attr in acquired)
            {
                await _throttleRegistry.ReleaseAsync(attr.Resource, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

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

        var retryAt = _retryPolicy.ComputeRetryAt(job, ex);

        if (retryAt is not null)
        {
            _logger.LogInformation(
                "Job {JobId} scheduled for retry at {RetryAt}",
                job.Id, retryAt.Value);
        }
        else
        {
            _logger.LogError(ex,
                "Job {JobId} exhausted all attempts - moving to dead-letter",
                job.Id);
        }

        return await Task.FromResult(retryAt).ConfigureAwait(false);
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
}
