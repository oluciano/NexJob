using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Unit tests for <see cref="JobDispatcherService"/> that cover behaviour
/// not already exercised by the end-to-end integration tests:
/// recurring-job status tracking, dead-letter transitions, and heartbeat updates.
/// These tests use <see cref="InMemoryStorageProvider"/> and a real DI host so
/// job dispatch runs through the full execution pipeline without Docker.
/// </summary>
public sealed class JobDispatcherServiceTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static IHost BuildHost(
        Action<IServiceCollection> registerJobs,
        int maxAttempts = 10,
        int workers = 2)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = workers;
                    opt.MaxAttempts = maxAttempts;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                });
                registerJobs(services);
            })
            .Build();
    }

    // ─── successful execution ─────────────────────────────────────────────────

    [Fact]
    public async Task SuccessfulJob_StatusBecomesSucceeded()
    {
        var tcs = new TaskCompletionSource<bool>();

        using var host = BuildHost(s => s.AddTransient(_ => new QuickSuccessJob(tcs)));
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<QuickSuccessJob, QuickInput>(new());

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();

        // Behavior changed in v5.10: wait until the dispatcher stored Succeeded instead of a fixed 50 ms (#371).
        await TestWait.SucceededAsync(storage, 1);
        var metrics = await storage.GetMetricsAsync();
        metrics.Succeeded.Should().Be(1);

        await host.StopAsync();
    }

    // ─── recurring job status tracking ───────────────────────────────────────

    [Fact]
    public async Task RecurringJob_WhenSucceeds_LastExecutionStatusIsSucceeded()
    {
        var tcs = new TaskCompletionSource<bool>();

        using var host = BuildHost(s => s.AddTransient(_ => new QuickSuccessJob(tcs)));
        await host.StartAsync();

        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();

        // Register a recurring job definition
        await storage.UpsertRecurringJobAsync(new RecurringJobRecord
        {
            RecurringJobId = "daily-success",
            JobType = typeof(QuickSuccessJob).AssemblyQualifiedName!,
            InputType = typeof(QuickInput).AssemblyQualifiedName!,
            InputJson = "{}",
            Cron = "* * * * *",
            Queue = "default",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // Manually enqueue a job instance with RecurringJobId set (what RecurringJobSchedulerService does)
        await storage.EnqueueAsync(new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(QuickSuccessJob).AssemblyQualifiedName!,
            InputType = typeof(QuickInput).AssemblyQualifiedName!,
            InputJson = "{}",
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.Enqueued,
            MaxAttempts = 10,
            RecurringJobId = "daily-success",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Behavior changed in v5.10: wait until the last execution result is stored instead of a fixed 50 ms (#371).
        await TestWait.UntilAsync(async () => (await storage.GetRecurringJobsAsync())
            .Single(r => r.RecurringJobId == "daily-success").LastExecutionStatus is not null);

        var all = await storage.GetRecurringJobsAsync();
        all.Single(r => r.RecurringJobId == "daily-success")
           .LastExecutionStatus.Should().Be(JobStatus.Succeeded);

        await host.StopAsync();
    }

    [Fact]
    public async Task RecurringJob_WhenDeadLettered_LastExecutionStatusIsFailed()
    {
        var tcs = new TaskCompletionSource<bool>();

        using var host = BuildHost(
            s => s.AddTransient(_ => new AlwaysFailJob(tcs)),
            maxAttempts: 1); // dead-letter immediately on first failure

        await host.StartAsync();

        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();

        await storage.UpsertRecurringJobAsync(new RecurringJobRecord
        {
            RecurringJobId = "daily-fail",
            JobType = typeof(AlwaysFailJob).AssemblyQualifiedName!,
            InputType = typeof(FailInput).AssemblyQualifiedName!,
            InputJson = "{}",
            Cron = "* * * * *",
            Queue = "default",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await storage.EnqueueAsync(new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(AlwaysFailJob).AssemblyQualifiedName!,
            InputType = typeof(FailInput).AssemblyQualifiedName!,
            InputJson = "{}",
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.Enqueued,
            MaxAttempts = 1,
            RecurringJobId = "daily-fail",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // Wait until the job is dead-lettered
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Behavior changed in v5.10: wait until the last execution result is stored instead of a fixed 100 ms (#371).
        await TestWait.UntilAsync(async () => (await storage.GetRecurringJobsAsync())
            .Single(r => r.RecurringJobId == "daily-fail").LastExecutionStatus is not null);

        var all = await storage.GetRecurringJobsAsync();
        var rec = all.Single(r => r.RecurringJobId == "daily-fail");
        rec.LastExecutionStatus.Should().Be(JobStatus.Failed);
        rec.LastExecutionError.Should().NotBeNullOrEmpty();

        await host.StopAsync();
    }

    // ─── retry scheduling ─────────────────────────────────────────────────────

    [Fact]
    public async Task FailedJob_SchedulesRetry_WhenAttemptsRemaining()
    {
        // Use maxAttempts=3 so the first failure still has retries left.
        // Use a fast, deterministic RetryDelayFactory to avoid real-time waits.
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.MaxAttempts = 3;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                    // Fixed short delay — just long enough to observe Scheduled state before promotion
                    opt.RetryDelayFactory = _ => TimeSpan.FromSeconds(60);
                });
                services.AddTransient<AlwaysFailJobForRetry>();
            })
            .Build();

        await host.StartAsync();

        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        await scheduler.EnqueueAsync<AlwaysFailJobForRetry, RetryInput>(new());

        // Wait until the dispatcher has processed the first attempt and called SetFailedAsync
        await Task.Delay(300);

        var metrics = await storage.GetMetricsAsync();

        // After one failure with retries remaining the job must be in Scheduled state (not Failed/Succeeded)
        metrics.Failed.Should().Be(0, "job has retries remaining so it must not move to dead-letter yet");
        metrics.Succeeded.Should().Be(0);

        await host.StopAsync();
    }

    [Fact]
    public async Task FailedJob_NoRetry_WhenMaxAttemptsExhausted()
    {
        // maxAttempts=1 → dead-letter on first failure
        var tcs = new TaskCompletionSource<bool>();

        using var host = BuildHost(
            s => s.AddTransient(_ => new AlwaysFailJob(tcs)),
            maxAttempts: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<AlwaysFailJob, FailInput>(new());

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Behavior changed in v5.6.2: a fixed 100 ms delay raced with the commit of the failure on slow CI runners,
        // so wait (bounded) until the dead-letter result is committed. The assertion below is unchanged.
        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        var metrics = await WaitForFailedAsync(storage, expected: 1);

        metrics.Failed.Should().Be(1, "job must be dead-lettered when MaxAttempts is exhausted");
        metrics.Succeeded.Should().Be(0);

        await host.StopAsync();
    }

    private static async Task<JobMetrics> WaitForFailedAsync(InMemoryStorageProvider storage, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var metrics = await storage.GetMetricsAsync();
        while (metrics.Failed < expected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
            metrics = await storage.GetMetricsAsync();
        }

        return metrics;
    }

    // ─── dead-letter ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Job_ExceedingMaxAttempts_MovesToDeadLetter()
    {
        var tcs = new TaskCompletionSource<bool>();

        using var host = BuildHost(
            s => s.AddTransient(_ => new AlwaysFailJob(tcs)),
            maxAttempts: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<AlwaysFailJob, FailInput>(new());

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Behavior changed in v5.6.2: a fixed 50 ms delay raced with the commit of the failure on slow CI runners,
        // so wait (bounded) until the dead-letter result is committed. The assertion below is unchanged.
        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        var metrics = await WaitForFailedAsync(storage, expected: 1);
        metrics.Failed.Should().Be(1);

        await host.StopAsync();
    }

    // ─── StopAsync timeout warning ────────────────────────────────────────────

    [Fact]
    public async Task StopAsync_WithActiveJobs_LogsWarningOnTimeout()
    {
        // A job that starts, signals, and then blocks indefinitely
        var jobStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseJob = new SemaphoreSlim(0, 1); // never released

        var logSink = new ListLoggerProvider();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.AddProvider(logSink))
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.MaxAttempts = 1;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                    opt.ShutdownTimeout = TimeSpan.FromMilliseconds(100);
                });
                services.AddTransient(_ => new GateableJob(jobStarted, releaseJob));
            })
            .Build();

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<GateableJob, GateableInput>(new());

        await jobStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await host.StopAsync();

        logSink.Messages.Should().Contain(m => m.Contains("Shutdown timeout") && m.Contains("still active"),
            "a warning must be logged when active jobs remain after ShutdownTimeout");

        releaseJob.Release();
    }

    // ─── all queues paused ────────────────────────────────────────────────────

    [Fact]
    public async Task Dispatcher_AllQueuesPaused_SkipsPolling()
    {
        var tcs = new TaskCompletionSource<bool>();
        using var host = BuildHost(s => s.AddTransient(_ => new QuickSuccessJob(tcs)));
        await host.StartAsync();

        var control = host.Services.GetRequiredService<IJobControlService>();
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();

        // Pause before enqueuing so the dispatcher never picks it up
        await control.PauseQueueAsync("default");
        var jobId = await scheduler.EnqueueAsync<QuickSuccessJob, QuickInput>(new());

        await Task.Delay(300);

        var job = await storage.GetJobByIdAsync(jobId);
        job!.Status.Should().Be(JobStatus.Enqueued, "dispatcher must skip paused queues");
        tcs.Task.IsCompleted.Should().BeFalse();

        await host.StopAsync();
    }

    // ─── FetchNextAsync throws ────────────────────────────────────────────────

    [Fact]
    public async Task Dispatcher_FetchNextThrows_LogsErrorAndContinues()
    {
        var logSink = new ListLoggerProvider();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.AddProvider(logSink))
            .ConfigureServices(services =>
            {
                // Register ThrowOnceJobStorage BEFORE AddNexJob so TryAdd skips it
                services.AddSingleton<IJobStorage>(sp =>
                    new ThrowOnceJobStorage(sp.GetRequiredService<InMemoryStorageProvider>()));

                services.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.MaxAttempts = 1;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                });
            })
            .Build();

        await host.StartAsync();

        // Allow at least one poll cycle to trigger the throw + one recovery cycle
        await Task.Delay(300);

        await host.StopAsync();

        logSink.Messages.Should().Contain(m => m.Contains("Error fetching next job"),
            "dispatcher must log an error when FetchNextAsync throws");
    }

    /// <summary>N1 (#377): a prefix derived from the assembly name is announced with guidance to set it explicitly.</summary>
    [Fact]
    public async Task Startup_WithDerivedQueuePrefix_LogsWarning()
    {
        var sink = new LevelLogSink();
        using var host = BuildPrefixHost(sink, _ => { });

        await host.StartAsync();
        await host.StopAsync();

        sink.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("QueuePrefix", StringComparison.Ordinal));
    }

    /// <summary>N2/N3 (#377): an explicit prefix does not warn; a whitespace-only one is treated as unset and does.</summary>
    /// <param name="prefix">The configured prefix.</param>
    /// <param name="warns">Whether the derived-prefix warning is expected.</param>
    [Theory]
    [InlineData("billing", false)]
    [InlineData("   ", true)]
    public async Task Startup_WithConfiguredQueuePrefix_WarnsOnlyWhenDerived(string prefix, bool warns)
    {
        var sink = new LevelLogSink();
        using var host = BuildPrefixHost(sink, o => o.QueuePrefix = prefix);

        await host.StartAsync();
        await host.StopAsync();

        sink.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("QueuePrefix", StringComparison.Ordinal))
            .Should().Be(warns);
    }

    /// <summary>N1/N2 (#377): without an entry assembly and without a prefix the shared default is back; say so, unless a prefix is set.</summary>
    /// <param name="prefix">The configured prefix.</param>
    /// <param name="warns">Whether the no-prefix warning is expected.</param>
    [Theory]
    [InlineData(null, true)]
    [InlineData("billing", false)]
    public async Task Startup_WithoutEntryAssembly_WarnsOnlyWhenNoPrefixIsSet(string? prefix, bool warns)
    {
        var sink = new LevelLogSink();
        using var host = BuildPrefixHost(sink, o =>
        {
            o.EntryAssembly = null;
            o.QueuePrefix = prefix;
        });

        await host.StartAsync();
        await host.StopAsync();

        sink.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("No default queue prefix", StringComparison.Ordinal))
            .Should().Be(warns);
    }

    private static IHost BuildPrefixHost(ILoggerProvider sink, Action<NexJobOptions> configure) =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.AddProvider(sink))
            .ConfigureServices(services => services.AddNexJob(configure))
            .Build();
}

// ─── Stub jobs ────────────────────────────────────────────────────────────────

public record QuickInput;

public sealed class QuickSuccessJob(TaskCompletionSource<bool> signal) : IJob<QuickInput>
{
    public Task ExecuteAsync(QuickInput input, CancellationToken cancellationToken)
    {
        signal.TrySetResult(true);
        return Task.CompletedTask;
    }
}

public record FailInput;

public record RetryInput;

/// <summary>
/// Always throws so we can observe that the dispatcher schedules a retry
/// rather than immediately dead-lettering the job.
/// </summary>
public sealed class AlwaysFailJobForRetry : IJob<RetryInput>
{
    /// <inheritdoc/>
    public Task ExecuteAsync(RetryInput input, CancellationToken cancellationToken)
        => throw new InvalidOperationException("intentional failure for retry test");
}

public sealed class AlwaysFailJob(TaskCompletionSource<bool> signalOnDeadLetter) : IJob<FailInput>
{
    public Task ExecuteAsync(FailInput input, CancellationToken cancellationToken)
    {
        // Signal only when we know we'll be dead-lettered (MaxAttempts=1 means this IS the last attempt)
        signalOnDeadLetter.TrySetResult(true);
        throw new InvalidOperationException("intentional failure");
    }
}

// ─── ThrowOnceJobStorage ──────────────────────────────────────────────────────

/// <summary>
/// Wraps InMemoryStorageProvider and throws once on the first FetchNextAsync call,
/// then delegates normally. Used to verify the dispatcher recovers from storage errors.
/// </summary>
internal sealed class ThrowOnceJobStorage : IJobStorage
{
    private readonly InMemoryStorageProvider _inner;
    private int _fetchCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThrowOnceJobStorage"/> class.
    /// </summary>
    /// <param name="inner">The inner storage provider.</param>
    public ThrowOnceJobStorage(InMemoryStorageProvider inner) => _inner = inner;

    /// <inheritdoc/>
    public Task<JobRecord?> FetchNextAsync(IReadOnlyList<string> queues, CancellationToken cancellationToken = default)
    {
        if (System.Threading.Interlocked.Increment(ref _fetchCount) == 1)
        {
            throw new InvalidOperationException("simulated storage failure");
        }

        return _inner.FetchNextAsync(queues, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<EnqueueResult> EnqueueAsync(JobRecord job, DuplicatePolicy duplicatePolicy = DuplicatePolicy.AllowAfterFailed, CancellationToken cancellationToken = default)
        => _inner.EnqueueAsync(job, duplicatePolicy, cancellationToken);

    /// <inheritdoc/>
    public Task AcknowledgeAsync(JobId jobId, CancellationToken cancellationToken = default)
        => _inner.AcknowledgeAsync(jobId, cancellationToken);

    /// <inheritdoc/>
    public Task SetFailedAsync(JobId jobId, Exception exception, DateTimeOffset? retryAt, CancellationToken cancellationToken = default)
        => _inner.SetFailedAsync(jobId, exception, retryAt, cancellationToken);

    /// <inheritdoc/>
    public Task SetExpiredAsync(JobId jobId, CancellationToken cancellationToken = default)
        => _inner.SetExpiredAsync(jobId, cancellationToken);

    /// <inheritdoc/>
    public Task UpdateHeartbeatAsync(JobId jobId, CancellationToken cancellationToken = default)
        => _inner.UpdateHeartbeatAsync(jobId, cancellationToken);

    /// <inheritdoc/>
    public Task CommitJobResultAsync(JobId jobId, JobExecutionResult result, CancellationToken cancellationToken = default)
        => _inner.CommitJobResultAsync(jobId, result, cancellationToken);

    /// <inheritdoc/>
    public Task RequeueOrphanedJobsAsync(TimeSpan heartbeatTimeout, CancellationToken cancellationToken = default)
        => _inner.RequeueOrphanedJobsAsync(heartbeatTimeout, cancellationToken);

    /// <inheritdoc/>
    public Task EnqueueContinuationsAsync(JobId parentJobId, CancellationToken cancellationToken = default)
        => _inner.EnqueueContinuationsAsync(parentJobId, cancellationToken);

    /// <inheritdoc/>
    public Task ReportProgressAsync(JobId jobId, int percent, string? message, CancellationToken ct = default)
        => _inner.ReportProgressAsync(jobId, percent, message, ct);

    /// <inheritdoc/>
    public Task<int> PurgeJobsAsync(RetentionPolicy policy, CancellationToken cancellationToken = default)
        => _inner.PurgeJobsAsync(policy, cancellationToken);

    /// <inheritdoc/>
    public Task RegisterServerAsync(ServerRecord server, CancellationToken cancellationToken = default)
        => _inner.RegisterServerAsync(server, cancellationToken);

    /// <inheritdoc/>
    public Task HeartbeatServerAsync(string serverId, CancellationToken cancellationToken = default)
        => _inner.HeartbeatServerAsync(serverId, cancellationToken);

    /// <inheritdoc/>
    public Task DeregisterServerAsync(string serverId, CancellationToken cancellationToken = default)
        => _inner.DeregisterServerAsync(serverId, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<ServerRecord>> GetActiveServersAsync(TimeSpan activeTimeout, CancellationToken cancellationToken = default)
        => _inner.GetActiveServersAsync(activeTimeout, cancellationToken);
}
