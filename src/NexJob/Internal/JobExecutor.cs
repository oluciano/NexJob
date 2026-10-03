using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NexJob.Exceptions;
using NexJob.Storage;
using NexJob.Telemetry;

namespace NexJob.Internal;

/// <summary>
/// Orchestrates the execution of a single NexJob job, including deadline enforcement,
/// DI scope management, input deserialization, throttling, and failure handling.
/// </summary>
internal sealed class JobExecutor : IDisposable, IAsyncDisposable
{
    // How long a job returned for lack of a throttle slot stays out of the queue (a little jitter is added).
    private static readonly TimeSpan ThrottleRequeueDelay = TimeSpan.FromSeconds(1);

    private readonly IJobStorage _storage;
    private readonly IJobInvokerFactory _invokerFactory;
    private readonly IJobRetryPolicy _retryPolicy;
    private readonly IDeadLetterDispatcher _deadLetterDispatcher;
    private readonly ThrottleRegistry _throttleRegistry;
    private readonly NexJobOptions _options;
    private readonly ILogger<JobExecutor> _logger;
    private readonly IReadOnlyList<IJobExecutionFilter> _filters;
    private readonly Channel<JobId> _ackChannel;
    private readonly CancellationTokenSource _ackCts;
    private readonly Task _ackFlusherTask;

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

        _ackChannel = Channel.CreateUnbounded<JobId>(new UnboundedChannelOptions { SingleReader = true });
        _ackCts = new CancellationTokenSource();
        _ackFlusherTask = Task.Run(RunBatchAckFlusherAsync);
    }

    /// <summary>Gets the delays between retries of a failed success commit. Overridable for tests.</summary>
    internal TimeSpan[] CommitRetryDelays { get; init; } =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(2),
    ];

    /// <inheritdoc/>
    public void Dispose()
    {
        _ackChannel.Writer.TryComplete();
        _ackCts.Cancel();
        _ackCts.Dispose();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _ackChannel.Writer.Complete();
        await _ackCts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _ackFlusherTask.ConfigureAwait(false);
        }
        catch
        {
            // Ignore cancel exceptions during shutdown
        }

        _ackCts.Dispose();
    }

    /// <summary>
    /// Executes the job asynchronously.
    /// </summary>
    /// <param name="job">The job.</param>
    /// <param name="cancellationToken">A cancellation token to observe while executing the job.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ExecuteJobAsync(JobRecord job, CancellationToken cancellationToken = default)
    {
        if (await TryHandleExpirationAsync(job).ConfigureAwait(false))
        {
            return;
        }

        _logger.LogDebug("Executing job {JobId} ({JobType}), attempt {Attempt}/{Max}",
            job.Id, job.JobType, job.Attempts, job.MaxAttempts);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = RunHeartbeatAsync(job.Id, cts.Token);

        using var loggingScope = _logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["NexJob.JobId"] = job.Id.Value,
            ["NexJob.JobType"] = job.JobType,
            ["NexJob.Queue"] = job.Queue,
            ["NexJob.Attempt"] = job.Attempts,
            ["NexJob.TraceParent"] = job.TraceParent ?? string.Empty,
        });

        using var logScope = new JobExecutionLogScope(_options.MaxJobLogLines);
        using var activity = NexJobActivitySource.StartExecute(job.JobType, job.Queue, job.TraceParent);
        activity?.SetTag("nexjob.job_id", job.Id.Value.ToString());
        activity?.SetTag("nexjob.attempt", job.Attempts);
        var sw = Stopwatch.StartNew();
        JobExecutionResult? successResult = null;
        var useBatchAck = false;

        try
        {
            using var context = await _invokerFactory.PrepareAsync(job, cts.Token).ConfigureAwait(false);
            await ExecuteWithThrottlingAndFiltersAsync(context, job, cts.Token).ConfigureAwait(false);

            sw.Stop();
            RecordSuccessMetrics(job.JobType, sw.Elapsed);
            activity?.SetStatus(ActivityStatusCode.Ok);

            var purgeOnSuccess = context.RetentionAttribute?.PurgeOnSuccess == true;
            var trimPayloadOnSuccess = context.RetentionAttribute?.TrimPayloadOnSuccess == true;

            useBatchAck = _options.EnableBatchAcknowledgment && !purgeOnSuccess && !trimPayloadOnSuccess && job.RecurringJobId is null && job.ParentJobId is null && logScope.Entries.Count == 0;
            successResult = new JobExecutionResult
            {
                Succeeded = true,
                Logs = logScope.Entries,
                RecurringJobId = job.RecurringJobId,
                PurgeOnSuccess = purgeOnSuccess,
                TrimPayloadOnSuccess = trimPayloadOnSuccess,
            };
        }
        catch (ForeignJobTypeException ex)
        {
            sw.Stop();
            // Foreign job: the job type or input type cannot be resolved in this process/service.
            // Do not penalize attempts or move to dead-letter. Defer with backoff so the owning service can execute it.
            // The attempt is given back by the storage (RefundAttempt): editing the local copy is not persisted.
            var retryAt = DateTimeOffset.UtcNow + _options.ForeignJobRetryDelay;
            _logger.LogWarning(
                ex,
                "Job {JobId} references foreign type '{TypeName}' not available in this host. Deferring until {RetryAt} without penalizing attempts.",
                job.Id,
                ex.TypeName,
                retryAt);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("nexjob.foreign_job", true);

            await _storage.CommitJobResultAsync(job.Id, new JobExecutionResult
            {
                Succeeded = false,
                Logs = logScope.Entries,
                Exception = ex,
                RetryAt = retryAt,
                RecurringJobId = job.RecurringJobId,
                RefundAttempt = true,
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (ThrottleDeferredException ex)
        {
            sw.Stop();
            // The throttled resource stayed saturated: free the worker slot instead of keeping it while waiting.
            // The attempt is refunded by the storage, so this never counts towards MaxAttempts or dead-letter.
            var retryAt = DateTimeOffset.UtcNow
                + ThrottleRequeueDelay
                + TimeSpan.FromMilliseconds(System.Security.Cryptography.RandomNumberGenerator.GetInt32(500));

            _logger.LogInformation(
                ex,
                "Job {JobId} ({JobType}) got no slot for throttled resource '{Resource}' within {Waited}s. Returning it to the queue until {RetryAt} without consuming an attempt.",
                job.Id,
                job.JobType,
                ex.Resource,
                ex.Waited.TotalSeconds,
                retryAt);

            activity?.SetTag("nexjob.throttle_deferred", true);
            NexJobMetrics.JobsThrottleDeferred.Add(1, new TagList { { "nexjob.job_type", job.JobType }, { "nexjob.resource", ex.Resource } });

            await _storage.CommitJobResultAsync(job.Id, new JobExecutionResult
            {
                Succeeded = false,
                Logs = logScope.Entries,
                Exception = ex,
                RetryAt = retryAt,
                RecurringJobId = job.RecurringJobId,
                RefundAttempt = true,
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            // Interrupted by shutdown: the job did not fail, the host stopped. Requeue immediately without
            // consuming the attempt (RefundAttempt) and never dead-letter, so it runs again on the next start.
            _logger.LogWarning(
                ex,
                "Job {JobId} ({JobType}) interrupted by shutdown. Requeuing without consuming the attempt.",
                job.Id,
                job.JobType);

            activity?.SetStatus(ActivityStatusCode.Error, "interrupted by shutdown");
            activity?.SetTag("nexjob.interrupted", true);

            await _storage.CommitJobResultAsync(job.Id, new JobExecutionResult
            {
                Succeeded = false,
                Logs = logScope.Entries,
                Exception = ex,
                RetryAt = DateTimeOffset.UtcNow,
                RecurringJobId = job.RecurringJobId,
                RefundAttempt = true,
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var retryAt = HandleFailureAsync(job, ex, activity);

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
            try
            {
                await heartbeatTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat loop for job {JobId} ended with an error", job.Id);
            }

            // Committed outside the job try/catch: a storage error here must never turn a job that
            // ran successfully into a failure or a dead-letter.
            if (successResult is not null)
            {
                await CommitSuccessAsync(job, successResult, useBatchAck).ConfigureAwait(false);
            }
        }
    }

    private static void RecordSuccessMetrics(string jobType, TimeSpan elapsed)
    {
        NexJobMetrics.JobDuration.Record(elapsed.TotalMilliseconds,
            new TagList { { "nexjob.job_type", jobType }, { "nexjob.status", "succeeded" } });
        NexJobMetrics.JobsSucceeded.Add(1,
            new TagList { { "nexjob.job_type", jobType } });
    }

    private async Task CommitSuccessAsync(JobRecord job, JobExecutionResult result, bool useBatchAck)
    {
        if (useBatchAck)
        {
            _ackChannel.Writer.TryWrite(job.Id);
            _logger.LogDebug("Job {JobId} completed successfully", job.Id);
            return;
        }

        for (var attempt = 0; attempt <= CommitRetryDelays.Length; attempt++)
        {
            try
            {
                await _storage.CommitJobResultAsync(job.Id, result, CancellationToken.None).ConfigureAwait(false);
                _logger.LogDebug("Job {JobId} completed successfully", job.Id);
                return;
            }
            catch (Exception ex)
            {
                if (attempt >= CommitRetryDelays.Length)
                {
                    // At-least-once: leave the job Processing so the orphan watcher runs it again, as after a crash.
                    _logger.LogError(
                        ex,
                        "Job {JobId} succeeded but its result could not be committed after {Attempts} attempts. It stays Processing and will be re-run by the orphan watcher.",
                        job.Id,
                        attempt + 1);
                    return;
                }

                _logger.LogWarning(
                    ex,
                    "Committing the success of job {JobId} failed (attempt {Attempt}). Retrying in {Delay}ms.",
                    job.Id,
                    attempt + 1,
                    CommitRetryDelays[attempt].TotalMilliseconds);
                await Task.Delay(CommitRetryDelays[attempt], CancellationToken.None).ConfigureAwait(false);
            }
        }
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
        var waitStarted = Stopwatch.StartNew();

        try
        {
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
                    // Waiting keeps this job's worker slot, so the wait is bounded: past it, hand the slot back.
                    if (waitStarted.Elapsed >= _options.ThrottleMaxWait)
                    {
                        throw new ThrottleDeferredException(attr.Resource, waitStarted.Elapsed);
                    }

                    // Slot still taken: back off (with jitter) instead of spinning against the throttle store.
                    await Task.Delay(TimeSpan.FromMilliseconds(250 + System.Security.Cryptography.RandomNumberGenerator.GetInt32(100)), cancellationToken).ConfigureAwait(false);
                }

                acquired.Add(attr);
            }

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

    private DateTimeOffset? HandleFailureAsync(JobRecord job, Exception ex, Activity? activity)
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

        return retryAt;
    }

    private async Task RunHeartbeatAsync(JobId jobId, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_options.HeartbeatInterval, cancellationToken).ConfigureAwait(false);
                try
                {
                    await _storage.UpdateHeartbeatAsync(jobId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A transient storage error must not end the heartbeat, or the orphan watcher would re-run a healthy job.
                    _logger.LogWarning(ex, "Heartbeat update for job {JobId} failed; will retry on the next interval", jobId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on job completion
        }
    }

    private async Task RunBatchAckFlusherAsync()
    {
        var buffer = new List<JobId>(100);
        while (!_ackCts.Token.IsCancellationRequested)
        {
            try
            {
                if (await _ackChannel.Reader.WaitToReadAsync(_ackCts.Token).ConfigureAwait(false))
                {
                    while (buffer.Count < 100 && _ackChannel.Reader.TryRead(out var id))
                    {
                        buffer.Add(id);
                    }

                    if (buffer.Count > 0)
                    {
                        await _storage.AcknowledgeBatchAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                        buffer.Clear();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing batch acknowledgments to storage");
                await Task.Delay(50, CancellationToken.None).ConfigureAwait(false);
            }
        }

        // Drain any leftovers
        while (_ackChannel.Reader.TryRead(out var leftoverId))
        {
            buffer.Add(leftoverId);
        }

        if (buffer.Count > 0)
        {
            await _storage.AcknowledgeBatchAsync(buffer, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
