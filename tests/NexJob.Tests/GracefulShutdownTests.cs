using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Tests for graceful shutdown behaviour of <see cref="JobDispatcherService"/>.
/// Verifies that active jobs are given a chance to complete during host shutdown,
/// and that the shutdown timeout is respected when jobs take too long.
/// </summary>
public sealed class GracefulShutdownTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static IHost BuildHost(
        Action<IServiceCollection> registerJobs,
        TimeSpan shutdownTimeout,
        int workers = 2)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = workers;
                    opt.MaxAttempts = 1;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                    opt.ShutdownTimeout = shutdownTimeout;
                });
                registerJobs(services);
            })
            .Build();
    }

    // ─── job completes cleanly before shutdown ────────────────────────────────

    [Fact]
    public async Task GracefulShutdown_JobCompletes_BeforeHostStops()
    {
        // Job runs for 600ms; shutdown timeout is 5s — job should complete before timeout
        var jobStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseJob = new SemaphoreSlim(0, 1);

        using var host = BuildHost(
            s => s.AddTransient(_ => new GateableJob(jobStarted, releaseJob)),
            shutdownTimeout: TimeSpan.FromSeconds(5));

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<GateableJob, GateableInput>(new());

        // Wait until the job has started executing
        await jobStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Now release the job after a short delay on a background thread
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            releaseJob.Release();
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await host.StopAsync();
        sw.Stop();

        // Job should have finished (Succeeded) and the shutdown should not have taken 5s
        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        var metrics = await storage.GetMetricsAsync();

        metrics.Succeeded.Should().Be(1, "job must complete when shutdown timeout is generous enough");
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(4),
            "host should stop as soon as the job finishes, not wait for the full timeout");
    }

    // ─── timeout abandons long-running job ────────────────────────────────────

    [Fact]
    public async Task GracefulShutdown_Timeout_AbandonesLongRunningJob()
    {
        // Job holds the gate indefinitely; shutdown timeout = 200ms
        var jobStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseJob = new SemaphoreSlim(0, 1); // never released

        var logSink = new ListLoggerProvider();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.AddProvider(logSink))
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 2;
                    opt.MaxAttempts = 1;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                    opt.ShutdownTimeout = TimeSpan.FromMilliseconds(200);
                });
                services.AddTransient(_ => new GateableJob(jobStarted, releaseJob));
            })
            .Build();

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<GateableJob, GateableInput>(new());

        await jobStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await host.StopAsync();
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "host must not wait more than ShutdownTimeout + overhead for a stuck job");

        logSink.Messages.Should().Contain(m => m.Contains("Shutdown timeout") && m.Contains("still active"),
            "a warning must be logged when jobs are still running after ShutdownTimeout");

        // Let the gate release so the test host cleans up properly
        releaseJob.Release();
    }

    // ─── zero timeout ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GracefulShutdown_ZeroTimeout_StopsImmediately()
    {
        var jobStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseJob = new SemaphoreSlim(0, 1); // never released

        using var host = BuildHost(
            s => s.AddTransient(_ => new GateableJob(jobStarted, releaseJob)),
            shutdownTimeout: TimeSpan.Zero);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<GateableJob, GateableInput>(new());

        await jobStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await host.StopAsync();
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
            "zero ShutdownTimeout must return almost immediately without deadlock");

        releaseJob.Release();
    }
}

// ─── Stub jobs ────────────────────────────────────────────────────────────────

/// <summary>Input for <see cref="GateableJob"/>.</summary>
public record GateableInput;

/// <summary>
/// A job that signals when it starts and then waits on a semaphore before completing.
/// Used to control when a job finishes during shutdown tests.
/// </summary>
public sealed class GateableJob(
    TaskCompletionSource<bool> startedSignal,
    SemaphoreSlim gate) : IJob<GateableInput>
{
    /// <inheritdoc/>
    public async Task ExecuteAsync(GateableInput input, CancellationToken cancellationToken)
    {
        startedSignal.TrySetResult(true);
        await gate.WaitAsync(cancellationToken);
    }
}

// ─── log capture helpers ──────────────────────────────────────────────────────

/// <summary>Captures log messages into a list for assertion.</summary>
internal sealed class ListLoggerProvider : ILoggerProvider
{
    private readonly List<string> _messages = [];

    /// <summary>All captured log messages.</summary>
    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
            {
                return [.._messages];
            }
        }
    }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new ListLogger(_messages);

    /// <inheritdoc/>
    public void Dispose()
    {
    }
}

internal sealed class ListLogger(List<string> messages) : ILogger
{
    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (messages)
        {
            messages.Add(formatter(state, exception));
        }
    }
}
