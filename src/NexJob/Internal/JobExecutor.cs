using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexJob.Exceptions;
using NexJob.Storage;
using NexJob.Telemetry;

namespace NexJob.Internal;

/// <summary>
/// Orchestrates the execution of a single NexJob job, including deadline enforcement,
/// DI scope management, input deserialization, throttling, and failure handling.
/// </summary>
internal sealed class JobExecutor : IJobExecutor, IDisposable, IAsyncDisposable
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
    private readonly JobWakeUpChannel _wakeUp;
    private readonly TimeProvider _timeProvider;
    private readonly JobHeartbeat _heartbeat;
    private readonly BatchAcknowledger _batchAck;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobExecutor"/> class.
    /// </summary>
    /// <param name="storage">The job storage.</param>
    /// <param name="invokerFactory">The job invoker factory.</param>
    /// <param name="retryPolicy">The job retry policy.</param>
    /// <param name="deadLetterDispatcher">The dead-letter dispatcher.</param>
    /// <param name="throttleRegistry">The throttle registry.</param>
    /// <param name="options">The nex job options.</param>
    /// <param name="wakeUp">The wake-up channel signalled after a successful commit, so promoted continuations are fetched without waiting a poll.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="timeProvider">The clock used for deadlines and retry times; the system clock when omitted.</param>
    public JobExecutor(
        IJobStorage storage,
        IJobInvokerFactory invokerFactory,
        IJobRetryPolicy retryPolicy,
        IDeadLetterDispatcher deadLetterDispatcher,
        ThrottleRegistry throttleRegistry,
        NexJobOptions options,
        JobWakeUpChannel wakeUp,
        ILogger<JobExecutor> logger,
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _wakeUp = wakeUp;
        _storage = storage;
        _invokerFactory = invokerFactory;
        _retryPolicy = retryPolicy;
        _deadLetterDispatcher = deadLetterDispatcher;
        _throttleRegistry = throttleRegistry;
        _options = options;
        _logger = logger;
        _heartbeat = new JobHeartbeat(storage, options, logger);

        _batchAck = new BatchAcknowledger(storage, wakeUp, logger);
    }

    /// <summary>Gets the delays between retries of a failed success commit. Overridable for tests.</summary>
    internal TimeSpan[] CommitRetryDelays { get; init; } =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(2),
    ];

    /// <inheritdoc/>
    public void Dispose() => _batchAck.Dispose();

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _batchAck.DisposeAsync();

    /// <inheritdoc/>
    public async Task ExecuteJobAsync(JobRecord job, CancellationToken cancellationToken = default)
    {
        if (await TryHandleExpirationAsync(job).ConfigureAwait(false))
        {
            return;
        }

        _logger.LogDebug("Executing job {JobId} ({JobType}), attempt {Attempt}/{Max}",
            job.Id, job.JobType, job.Attempts, job.MaxAttempts);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = _heartbeat.RunAsync(job.Id, cts.Token);

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
            var retryAt = _timeProvider.GetUtcNow() + _options.ForeignJobRetryDelay;
            _logger.LogWarning(
                ex,
                "Job {JobId} references foreign type '{TypeName}' not available in this host. Deferring until {RetryAt} without penalizing attempts.",
                job.Id,
                ex.TypeName,
                retryAt);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("nexjob.foreign_job", true);

            await CommitFailureAsync(job, logScope.Entries, ex, retryAt, refundAttempt: true).ConfigureAwait(false);
        }
        catch (ThrottleDeferredException ex)
        {
            sw.Stop();
            // The throttled resource stayed saturated: free the worker slot instead of keeping it while waiting.
            // The attempt is refunded by the storage, so this never counts towards MaxAttempts or dead-letter.
            var retryAt = _timeProvider.GetUtcNow()
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

            await CommitFailureAsync(job, logScope.Entries, ex, retryAt, refundAttempt: true).ConfigureAwait(false);
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

            await CommitFailureAsync(job, logScope.Entries, ex, _timeProvider.GetUtcNow(), refundAttempt: true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var retryAt = HandleFailureAsync(job, ex, activity);

            await CommitFailureAsync(job, logScope.Entries, ex, retryAt, refundAttempt: false).ConfigureAwait(false);

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

    private Task CommitFailureAsync(JobRecord job, IReadOnlyList<JobExecutionLog> logs, Exception exception, DateTimeOffset? retryAt, bool refundAttempt)
        => _storage.CommitJobResultAsync(job.Id, new JobExecutionResult
        {
            Succeeded = false,
            Logs = logs,
            Exception = exception,
            RetryAt = retryAt,
            RecurringJobId = job.RecurringJobId,
            RefundAttempt = refundAttempt,
        }, CancellationToken.None);

    private async Task CommitSuccessAsync(JobRecord job, JobExecutionResult result, bool useBatchAck)
    {
        if (useBatchAck)
        {
            _batchAck.Enqueue(job.Id);
            _logger.LogDebug("Job {JobId} completed successfully", job.Id);
            return;
        }

        for (var attempt = 0; attempt <= CommitRetryDelays.Length; attempt++)
        {
            try
            {
                await _storage.CommitJobResultAsync(job.Id, result, CancellationToken.None).ConfigureAwait(false);
                _wakeUp.Signal();
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
        if (!job.ExpiresAt.HasValue || _timeProvider.GetUtcNow() <= job.ExpiresAt.Value)
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
        var slots = await AcquireThrottleSlotsAsync(ctx, cancellationToken).ConfigureAwait(false);
        await using (slots.ConfigureAwait(false))
        {
            // The limit starts here, after the throttle slots are held, so waiting for a throttled resource never counts.
            using var timeout = new ExecutionTimeout(
                ctx.ExecutionTimeoutAttribute?.Timeout ?? _options.DefaultExecutionTimeout,
                cancellationToken,
                (limit, watchdogToken) => _ = WarnIfCancellationIgnoredAsync(job, limit, watchdogToken, cancellationToken));
            var jobToken = timeout.Token;

            // Terminal delegate: invokes the actual job. Converting here lets filters see the TimeoutException.
            JobExecutionDelegate jobInvoker = async ct =>
            {
                try
                {
                    await ctx.Invoker(ctx.JobInstance, ctx.Input, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (timeout.TimedOut)
                {
                    throw timeout.Failure(ex);
                }
            };

            try
            {
                // Resolved from the job scope, so a filter follows the lifetime it was registered with.
                var filters = ctx.Scope.ServiceProvider.GetServices<IJobExecutionFilter>().ToList();
                if (filters.Count == 0)
                {
                    // Fast path: no filters registered
                    await jobInvoker(jobToken).ConfigureAwait(false);
                }
                else
                {
                    var context = new JobExecutingContext(job, ctx.Scope.ServiceProvider);

                    var pipeline = JobFilterPipeline.Build(filters, context, jobInvoker);

                    try
                    {
                        await pipeline(jobToken).ConfigureAwait(false);
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
            catch (OperationCanceledException ex) when (timeout.TimedOut)
            {
                // A filter that observed the token itself, before the job ran.
                throw timeout.Failure(ex);
            }
        }
    }

    private async Task<ThrottleSlots> AcquireThrottleSlotsAsync(JobInvocationContext ctx, CancellationToken cancellationToken)
    {
        var slots = new ThrottleSlots(_throttleRegistry);
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

                slots.Add(attr);
            }
        }
        catch
        {
            await slots.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return slots;
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

        // A job that lacks the contract of its input fails the same way on every attempt: do not spend them.
        var retryAt = ex is JobContractException ? null : _retryPolicy.ComputeRetryAt(job, ex);

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

    private async Task WarnIfCancellationIgnoredAsync(JobRecord job, TimeSpan timeout, CancellationToken jobReturned, CancellationToken shutdown)
    {
        // The linked timeout token is also cancelled by a shutdown, which is not this job's problem.
        if (shutdown.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await Task.Delay(_options.CancellationGracePeriod, jobReturned).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // the job returned inside the grace period
        }

        if (shutdown.IsCancellationRequested)
        {
            return;
        }

        _logger.LogWarning(
            "Job {JobId} ({JobType}) ignored cancellation {Grace}s after its execution timeout of {Timeout} and still holds its worker slot.",
            job.Id,
            job.JobType,
            _options.CancellationGracePeriod.TotalSeconds,
            timeout);
        NexJobMetrics.JobsCancellationIgnored.Add(1, new TagList { { "nexjob.job_type", job.JobType } });
    }

    /// <summary>The throttle slots a job holds; disposing releases every one of them.</summary>
    private sealed class ThrottleSlots : IAsyncDisposable
    {
        private readonly ThrottleRegistry _registry;
        private readonly List<ThrottleAttribute> _held = [];

        public ThrottleSlots(ThrottleRegistry registry) => _registry = registry;

        public void Add(ThrottleAttribute attribute) => _held.Add(attribute);

        public async ValueTask DisposeAsync()
        {
            foreach (var attr in _held)
            {
                await _registry.ReleaseAsync(attr.Resource, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// The execution limit of one run. It uses its own CTS, so the heartbeat token and the shutdown token stay untouched,
    /// and arms a watchdog: a job that ignores the token keeps its worker slot, which is made visible once per run.
    /// </summary>
    private sealed class ExecutionTimeout : IDisposable
    {
        private readonly TimeSpan? _limit;
        private readonly CancellationToken _shutdown;
        private readonly CancellationTokenSource? _cts;
        private readonly CancellationTokenSource? _watchdogCts;
        private readonly CancellationTokenRegistration _watchdog;

        public ExecutionTimeout(TimeSpan? limit, CancellationToken shutdown, Action<TimeSpan, CancellationToken> onLimitReached)
        {
            _limit = limit;
            _shutdown = shutdown;
            if (limit is null)
            {
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
            _cts.CancelAfter(limit.Value);
            _watchdogCts = new CancellationTokenSource();
            _watchdog = _cts.Token.Register(() => onLimitReached(limit.Value, _watchdogCts.Token));
        }

        /// <summary>Gets the token the job runs under: the limit when there is one, the shutdown token otherwise.</summary>
        public CancellationToken Token => _cts?.Token ?? _shutdown;

        /// <summary>Gets a value indicating whether only the limit fired. Shutdown has priority and is handled by the caller: a normal failure, not an interruption.</summary>
        public bool TimedOut => _cts is { IsCancellationRequested: true } && !_shutdown.IsCancellationRequested;

        public TimeoutException Failure(OperationCanceledException ex) => new($"Job execution timed out after {_limit!.Value}.", ex);

        public void Dispose()
        {
            _watchdog.Dispose();
            _watchdogCts?.Cancel(); // stands the watchdog down as soon as the job returns
            _watchdogCts?.Dispose();
            _cts?.Dispose();
        }
    }
}
