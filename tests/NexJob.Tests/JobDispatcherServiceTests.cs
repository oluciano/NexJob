using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob;
using NexJob.Configuration;
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

    // ─── runtime settings read throws (#640) ──────────────────────────────────

    private static IHost BuildHostWithRuntimeStore(FlakyRuntimeSettingsStore store, ListLoggerProvider? logSink = null)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(l =>
            {
                if (logSink is not null)
                {
                    l.AddProvider(logSink);
                }
            })
            .ConfigureServices(services =>
            {
                // Registered BEFORE AddNexJob so TryAdd keeps this store
                services.AddSingleton<IRuntimeSettingsStore>(store);
                services.AddTransient(_ => new QuickSuccessJob(new TaskCompletionSource<bool>()));
                services.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.MaxAttempts = 1;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                });
            })
            .Build();
    }

    [Fact]
    public async Task Dispatcher_RuntimeSettingsReadThrowsThenRecovers_StillRunsTheJob()
    {
        var store = new FlakyRuntimeSettingsStore(failures: 2);
        using var host = BuildHostWithRuntimeStore(store);
        await host.StartAsync();

        await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<QuickSuccessJob, QuickInput>(new());

        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        await TestWait.SucceededAsync(storage, 1);

        store.Calls.Should().BeGreaterThan(2, "the dispatcher must keep reading after the failures");

        await host.StopAsync();
    }

    [Fact]
    public async Task Dispatcher_RuntimeSettingsReadAlwaysThrows_KeepsPollingAndLogsError()
    {
        var logSink = new ListLoggerProvider();
        var store = new FlakyRuntimeSettingsStore(failures: int.MaxValue);
        using var host = BuildHostWithRuntimeStore(store, logSink);
        await host.StartAsync();

        var jobId = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<QuickSuccessJob, QuickInput>(new());

        await TestWait.UntilAsync(() => Task.FromResult(store.Calls >= 3), because: "the polling loop must stay alive during the outage");

        logSink.Messages.Should().Contain(m => m.Contains("Error reading runtime settings", StringComparison.Ordinal));
        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        (await storage.GetJobByIdAsync(jobId))!.Status.Should().Be(JobStatus.Enqueued, "nothing can run while the settings cannot be read");

        await host.StopAsync();
    }

    [Fact]
    public async Task Dispatcher_CancelledWhileReadingRuntimeSettings_StopsCleanlyWithoutErrorLog()
    {
        var logSink = new ListLoggerProvider();
        var store = new FlakyRuntimeSettingsStore(failures: 0, blockUntilCancelled: true);
        using var host = BuildHostWithRuntimeStore(store, logSink);
        await host.StartAsync();

        await TestWait.UntilAsync(() => Task.FromResult(store.Calls >= 1), because: "the dispatcher must reach the blocking read");

        await host.StopAsync();

        logSink.Messages.Should().NotContain(m => m.Contains("Error reading runtime settings", StringComparison.Ordinal));
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

    /// <summary>Issue #623 N1: a host that is disposed and then stopped (WebApplicationFactory, TestServer) must not make StopAsync throw.</summary>
    [Fact]
    public async Task StopAsync_AfterDispose_DoesNotThrow()
    {
        using var host = BuildPrefixHost(new LevelLogSink(), _ => { });
        await host.StartAsync();
        var dispatcher = host.Services.GetServices<IHostedService>().OfType<JobDispatcherService>().Single();

        dispatcher.Dispose();
        var act = () => dispatcher.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    /// <summary>Issue #623 N3: disposing twice and stopping twice are both harmless.</summary>
    [Fact]
    public async Task DisposeAndStopAsync_CalledTwice_DoNotThrow()
    {
        using var host = BuildPrefixHost(new LevelLogSink(), _ => { });
        await host.StartAsync();
        var dispatcher = host.Services.GetServices<IHostedService>().OfType<JobDispatcherService>().Single();

        await dispatcher.StopAsync(CancellationToken.None);
        dispatcher.Dispose();
        dispatcher.Dispose();
        var act = () => dispatcher.StopAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static bool IsQueuePrefixWarning((LogLevel Level, string Message) entry) =>
        entry.Level == LogLevel.Warning && entry.Message.Contains("QueuePrefix", StringComparison.Ordinal);

    /// <summary>N1 (#377): a prefix derived from the assembly name is announced with guidance to set it explicitly.</summary>
    [Fact]
    public async Task Startup_WithDerivedQueuePrefix_LogsWarning()
    {
        var sink = new LevelLogSink();
        using var host = BuildPrefixHost(sink, _ => { });

        await host.StartAsync();

        // StartAsync does not guarantee the dispatcher ran to its first await: wait for the warning, then stop.
        await TestWait.UntilAsync(() => Task.FromResult(sink.Entries.Any(IsQueuePrefixWarning)));
        await host.StopAsync();

        sink.Entries.Where(IsQueuePrefixWarning).Should().ContainSingle();
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
        if (warns)
        {
            await TestWait.UntilAsync(() => Task.FromResult(sink.Entries.Any(IsQueuePrefixWarning)));
        }

        await host.StopAsync();

        sink.Entries.Any(IsQueuePrefixWarning).Should().Be(warns);
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

    /// <summary>N1 (#377): a job stored in the legacy "default" queue before the upgrade still runs on a host that has a prefix.</summary>
    [Fact]
    public async Task PrefixedHost_JobInLegacyDefault_StillRuns()
    {
        var ran = new TaskCompletionSource<bool>();
        using var host = BuildQueueHost(new LevelLogSink(), ran);
        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        await storage.EnqueueAsync(QuickRecord("default"));

        await host.StartAsync();
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await TestWait.SucceededAsync(storage, 1);

        await host.StopAsync();
        (await storage.GetMetricsAsync()).Succeeded.Should().Be(1);
    }

    /// <summary>N2 (#377): a job of another application in the legacy "default" queue is deferred, not executed and not dead-lettered.</summary>
    [Fact]
    public async Task PrefixedHost_ForeignJobInLegacyDefault_IsDeferredWithoutUsingAnAttempt()
    {
        var sink = new LevelLogSink();
        using var host = BuildQueueHost(sink, new TaskCompletionSource<bool>());
        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        var foreign = QuickRecord("default");
        var foreignRecord = new JobRecord
        {
            Id = foreign.Id,
            JobType = "Other.Service.Job, Other.Service",
            InputType = foreign.InputType,
            InputJson = foreign.InputJson,
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.Enqueued,
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = 10,
        };
        await storage.EnqueueAsync(foreignRecord);

        await host.StartAsync();
        await TestWait.UntilAsync(() => Task.FromResult(sink.Entries.Any(e => e.Message.Contains("Deferring", StringComparison.Ordinal))));
        await host.StopAsync();

        var stored = await storage.GetJobByIdAsync(foreignRecord.Id);
        stored!.Status.Should().Be(JobStatus.Scheduled, "a deferred foreign job waits for the owning service");
        stored.Attempts.Should().Be(0);
    }

    /// <summary>N2 (#377): a job in another application's prefixed queue is never fetched by this host.</summary>
    [Fact]
    public async Task PrefixedHost_JobInAnotherPrefixedQueue_IsNeverFetched()
    {
        var ran = new TaskCompletionSource<bool>();
        using var host = BuildQueueHost(new LevelLogSink(), ran);
        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        var other = QuickRecord("inventory.default");
        await storage.EnqueueAsync(other);
        await storage.EnqueueAsync(QuickRecord("billing.default"));

        await host.StartAsync();
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await TestWait.SucceededAsync(storage, 1);
        await host.StopAsync();

        (await storage.GetJobByIdAsync(other.Id))!.Status.Should().Be(JobStatus.Enqueued);
    }

    /// <summary>N1 (#402): the execution window configured for "default" also holds the legacy "default" back.</summary>
    [Fact]
    public async Task PrefixedHost_JobInLegacyDefault_OutsideTheConfiguredWindow_IsNotRun()
    {
        var ran = new TaskCompletionSource<bool>();
        var closedFrom = TimeOnly.FromDateTime(DateTime.UtcNow.AddHours(6));
        using var host = BuildQueueHost(
            new LevelLogSink(),
            ran,
            o =>
            {
                o.Workers = 1;
                o.Queues = ["default", "emails"];
                o.ConfigureQueue("default", q => q.ExecutionWindow = new ExecutionWindowSettings { StartTime = closedFrom, EndTime = closedFrom.AddHours(1), TimeZone = "UTC" });
            });
        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        var legacy = QuickRecord("default");
        await storage.EnqueueAsync(legacy);
        await storage.EnqueueAsync(QuickRecord("emails"));

        await host.StartAsync();

        // One worker and "emails" is read after the legacy queue: the first job to succeed shows what was fetched first.
        await TestWait.SucceededAsync(storage, 1);
        await host.StopAsync();
        (await storage.GetJobByIdAsync(legacy.Id))!.Status.Should().Be(JobStatus.Enqueued, "the window is closed for the default queue");
    }

    /// <summary>N2 (#472): an unknown time zone in an execution window stops the start and names the queue and the id.</summary>
    [Fact]
    public async Task ExecutionWindow_WithUnknownTimeZone_FailsTheStartNamingTheQueueAndTheId()
    {
        using var host = BuildQueueHost(new LevelLogSink(), new TaskCompletionSource<bool>(), o =>
        {
            o.Queues = ["batch", "other"];
            o.ConfigureQueue("batch", q => q.ExecutionWindow = AllDayWindow("Not/AZone"));
        });

        var act = () => host.StartAsync();

        var error = await act.Should().ThrowAsync<InvalidOperationException>();
        error.Which.Message.Should().Contain("batch").And.Contain("Not/AZone");
    }

    /// <summary>N3 (#472): an empty, blank or missing time zone is rejected at start like an unknown one.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task ExecutionWindow_WithEmptyBlankOrNullTimeZone_FailsTheStart(string? timeZone)
    {
        using var host = BuildQueueHost(new LevelLogSink(), new TaskCompletionSource<bool>(), o =>
            o.ConfigureQueue("default", q => q.ExecutionWindow = AllDayWindow(timeZone!)));

        var act = () => host.StartAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// N1 (#579): an unreadable time zone is rejected before the service starts, so it never logs "started" nor reaches the
    /// loop. Validating inside ExecuteAsync only fails the start on hosts where StartAsync runs its first part (Hosting 8).
    /// </summary>
    [Fact]
    public async Task ExecutionWindow_WithUnknownTimeZone_RejectsTheStartBeforeTheServiceStarts()
    {
        var sink = new LevelLogSink();
        using var host = BuildQueueHost(sink, new TaskCompletionSource<bool>(), o =>
            o.ConfigureQueue("default", q => q.ExecutionWindow = AllDayWindow("Not/AZone")));

        var act = () => host.StartAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
        sink.Entries.Should().NotContain(e => e.Message.Contains("JobDispatcherService started", StringComparison.Ordinal));
    }

    /// <summary>N3 (#579): a host that does not fetch jobs (Workers = 0) does not read the zones, so it starts.</summary>
    [Fact]
    public async Task ExecutionWindow_WithUnknownTimeZoneAndNoWorkers_StartsTheHost()
    {
        using var host = BuildQueueHost(new LevelLogSink(), new TaskCompletionSource<bool>(), o =>
        {
            o.Workers = 0;
            o.ConfigureQueue("default", q => q.ExecutionWindow = AllDayWindow("Not/AZone"));
        });

        var act = () => host.StartAsync();

        await act.Should().NotThrowAsync();
        await host.StopAsync();
    }

    /// <summary>N1 (#472): IANA and UTC ids still start, so the validation does not reject what worked.</summary>
    [Theory]
    [InlineData("UTC")]
    [InlineData("America/Sao_Paulo")]
    public async Task ExecutionWindow_WithAKnownTimeZone_StartsTheHost(string timeZone)
    {
        using var host = BuildQueueHost(new LevelLogSink(), new TaskCompletionSource<bool>(), o =>
            o.ConfigureQueue("default", q => q.ExecutionWindow = AllDayWindow(timeZone)));

        var act = () => host.StartAsync();

        await act.Should().NotThrowAsync();
        await host.StopAsync();
    }

    /// <summary>
    /// N2 (#472): if the zone becomes unreadable after the start, only that queue is closed. The other queue keeps running,
    /// and the error is logged once for the queue, not on every poll.
    /// </summary>
    [Fact]
    public async Task ExecutionWindow_WhenTheTimeZoneBreaksAfterStart_ClosesOnlyThatQueueAndLogsOnce()
    {
        var sink = new LevelLogSink();
        var ran = new TaskCompletionSource<bool>();
        var window = AllDayWindow("UTC");
        using var host = BuildQueueHost(sink, ran, o =>
        {
            o.Queues = ["batch", "other"];
            o.ConfigureQueue("batch", q => q.ExecutionWindow = window);
        });
        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        await host.StartAsync();
        window.TimeZone = "Not/AZone";
        var held = QuickRecord("batch");
        await storage.EnqueueAsync(held);
        await storage.EnqueueAsync(QuickRecord("other"));
        await storage.EnqueueAsync(QuickRecord("other"));

        await TestWait.SucceededAsync(storage, 2); // several polls ran after the zone broke
        await TestWait.UntilAsync(() => Task.FromResult(sink.Entries.Any(e => e.Level == LogLevel.Error && e.Message.Contains("Not/AZone", StringComparison.Ordinal))));
        await host.StopAsync();

        (await storage.GetJobByIdAsync(held.Id))!.Status.Should().Be(JobStatus.Enqueued, "a queue whose window cannot be read stays closed");
        sink.Entries.Count(e => e.Level == LogLevel.Error && e.Message.Contains("Not/AZone", StringComparison.Ordinal))
            .Should().Be(1, "the error is logged once per queue, not on every poll");
    }

    /// <summary>N1 (#404): Workers = 0 starts the host, says so, and leaves the job alone.</summary>
    [Fact]
    public async Task ZeroWorkers_HostStarts_AndDoesNotExecuteJobs()
    {
        var sink = new LevelLogSink();
        var ran = new TaskCompletionSource<bool>();
        using var host = BuildQueueHost(sink, ran, o => o.Workers = 0);
        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        var job = QuickRecord("billing.default");
        await storage.EnqueueAsync(job);

        await host.StartAsync();
        await TestWait.UntilAsync(
            () => Task.FromResult(sink.Entries.Any(e => e.Message.Contains("does not fetch or execute jobs", StringComparison.Ordinal))),
            because: "the dispatcher to say that this host executes nothing");
        await Task.Delay(300); // nothing may happen: the dispatcher polls every 20 ms, so this is many polling cycles
        await host.StopAsync();

        (await storage.GetJobByIdAsync(job.Id))!.Status.Should().Be(JobStatus.Enqueued);
        ran.Task.IsCompleted.Should().BeFalse();
    }

    /// <summary>N1/N2 (#515): when circuit breaker for a queue is OPEN, that queue is skipped and other queues proceed.</summary>
    [Fact]
    public async Task OpenCircuitBreaker_SkipsOpenQueueAndExecutesOtherQueues()
    {
        var sink = new LevelLogSink();
        var ran = new TaskCompletionSource<bool>();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.AddProvider(sink))
            .ConfigureServices(services =>
            {
                services.AddNexJob(o =>
                {
                    o.Workers = 2;
                    o.PollingInterval = TimeSpan.FromMilliseconds(20);
                    o.Queues = ["payments", "emails"];
                    o.ConfigureQueue("payments", q => q.CircuitBreaker = new QueueCircuitBreakerOptions
                    {
                        ConsecutiveFailuresThreshold = 1,
                        OpenDuration = TimeSpan.FromMinutes(5),
                    });
                });
                services.AddTransient(_ => new QuickSuccessJob(ran));
            })
            .Build();

        var breakerManager = host.Services.GetRequiredService<IQueueCircuitBreakerManager>();
        breakerManager.RecordOutcome("payments", false, new InvalidOperationException("boom"));
        breakerManager.GetState("payments", out _).Should().Be(QueueCircuitState.Open);

        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        var blockedPaymentJob = QuickRecord("payments");
        var activeEmailJob = QuickRecord("emails");
        await storage.EnqueueAsync(blockedPaymentJob);
        await storage.EnqueueAsync(activeEmailJob);

        await host.StartAsync();
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestWait.SucceededAsync(storage, 1);
        await host.StopAsync();

        (await storage.GetJobByIdAsync(blockedPaymentJob.Id))!.Status.Should().Be(JobStatus.Enqueued, "open circuit prevents fetching from payments");
        (await storage.GetJobByIdAsync(activeEmailJob.Id))!.Status.Should().Be(JobStatus.Succeeded, "emails queue is not blocked");
    }

    /// <summary>N1/N2 (#515): when circuit is HalfOpen and allowed concurrency is 0 (probe in flight), queue is skipped.</summary>
    [Fact]
    public async Task HalfOpenCircuit_WithZeroAllowedConcurrency_SkipsQueue()
    {
        var sink = new LevelLogSink();
        var ran = new TaskCompletionSource<bool>();
        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.AddProvider(sink))
            .ConfigureServices(services =>
            {
                services.AddNexJob(o =>
                {
                    o.Workers = 2;
                    o.PollingInterval = TimeSpan.FromMilliseconds(20);
                    o.Queues = ["payments", "emails"];
                    o.ConfigureQueue("payments", q => q.CircuitBreaker = new QueueCircuitBreakerOptions
                    {
                        ConsecutiveFailuresThreshold = 1,
                        OpenDuration = TimeSpan.FromSeconds(10),
                    });
                });
                services.AddSingleton<TimeProvider>(fakeTime);
                services.AddTransient(_ => new QuickSuccessJob(ran));
            })
            .Build();

        var breakerManager = host.Services.GetRequiredService<IQueueCircuitBreakerManager>();
        // Trip breaker to Open
        breakerManager.RecordOutcome("payments", false, new InvalidOperationException("boom"));
        breakerManager.GetState("payments", out _).Should().Be(QueueCircuitState.Open);

        // Advance time past OpenDuration to trigger transition to HalfOpen
        fakeTime.Advance(TimeSpan.FromSeconds(15));

        // First call transitions to HalfOpen with allowedConcurrency = 1 (probe dispatched)
        var state1 = breakerManager.GetState("payments", out var allowed1);
        state1.Should().Be(QueueCircuitState.HalfOpen);
        allowed1.Should().Be(1);

        // Second call while probe is in flight returns HalfOpen with allowedConcurrency = 0
        var state2 = breakerManager.GetState("payments", out var allowed2);
        state2.Should().Be(QueueCircuitState.HalfOpen);
        allowed2.Should().Be(0);

        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        var blockedPaymentJob = QuickRecord("payments");
        var activeEmailJob = QuickRecord("emails");
        await storage.EnqueueAsync(blockedPaymentJob);
        await storage.EnqueueAsync(activeEmailJob);

        await host.StartAsync();
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await TestWait.SucceededAsync(storage, 1);
        await host.StopAsync();

        (await storage.GetJobByIdAsync(blockedPaymentJob.Id))!.Status.Should().Be(JobStatus.Enqueued, "half-open with allowed=0 prevents fetching");
        (await storage.GetJobByIdAsync(activeEmailJob.Id))!.Status.Should().Be(JobStatus.Succeeded, "emails queue is not blocked");
    }

    private static ExecutionWindowSettings AllDayWindow(string timeZone) => new()
    {
        StartTime = new TimeOnly(0, 0),
        EndTime = new TimeOnly(23, 59),
        TimeZone = timeZone,
    };

    private static JobRecord QuickRecord(string queue) => new()
    {
        Id = JobId.New(),
        JobType = typeof(QuickSuccessJob).AssemblyQualifiedName!,
        InputType = typeof(QuickInput).AssemblyQualifiedName!,
        InputJson = "{}",
        Queue = queue,
        Priority = JobPriority.Normal,
        Status = JobStatus.Enqueued,
        CreatedAt = DateTimeOffset.UtcNow,
        MaxAttempts = 10,
    };

    private static IHost BuildQueueHost(ILoggerProvider sink, TaskCompletionSource<bool> ran, Action<NexJobOptions>? configure = null) =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.AddProvider(sink))
            .ConfigureServices(services =>
            {
                services.AddNexJob(o =>
                {
                    o.QueuePrefix = "billing";
                    o.Workers = 2;
                    o.PollingInterval = TimeSpan.FromMilliseconds(20);
                    configure?.Invoke(o);
                });
                services.AddTransient(_ => new QuickSuccessJob(ran));
            })
            .Build();

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

// ─── FlakyRuntimeSettingsStore ────────────────────────────────────────────────

/// <summary>
/// A runtime settings store whose reads throw a number of times and then succeed, or block until cancelled.
/// Used to verify the dispatcher survives a storage outage on the settings read.
/// </summary>
internal sealed class FlakyRuntimeSettingsStore : IRuntimeSettingsStore
{
    private readonly int _failures;
    private readonly bool _blockUntilCancelled;
    private int _calls;

    /// <summary>
    /// Initializes a new instance of the <see cref="FlakyRuntimeSettingsStore"/> class.
    /// </summary>
    /// <param name="failures">How many reads throw before they start succeeding.</param>
    /// <param name="blockUntilCancelled">When true, a read waits for its token to be cancelled.</param>
    public FlakyRuntimeSettingsStore(int failures, bool blockUntilCancelled = false)
    {
        _failures = failures;
        _blockUntilCancelled = blockUntilCancelled;
    }

    /// <summary>Gets the number of reads so far.</summary>
    public int Calls => System.Threading.Volatile.Read(ref _calls);

    /// <inheritdoc/>
    public async Task<RuntimeSettings> GetAsync(CancellationToken ct = default)
    {
        var call = System.Threading.Interlocked.Increment(ref _calls);

        if (_blockUntilCancelled)
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
        }

        if (call <= _failures)
        {
            throw new InvalidOperationException("simulated storage outage");
        }

        return new RuntimeSettings();
    }

    /// <inheritdoc/>
    public Task SaveAsync(RuntimeSettings settings, CancellationToken ct = default) => Task.CompletedTask;
}
