using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

public sealed class ThrottleEndToEndTests
{
    private static IHost BuildHost(Action<IServiceCollection> registerJobs, int workers = 10)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = workers;
                    opt.MaxAttempts = 1;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                });
                registerJobs(services);
            })
            .Build();
    }

    // ─── throttle enforces max-concurrent ────────────────────────────────────

    [Fact]
    public async Task ThrottledJob_NeverExceedsMaxConcurrent()
    {
        const int maxConcurrent = 2;
        const int totalJobs = 6;

        var concurrencyTracker = new ConcurrencyTracker();

        using var host = BuildHost(s =>
            s.AddTransient<ThrottledJob>(_ => new ThrottledJob(concurrencyTracker)));

        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        for (var i = 0; i < totalJobs; i++)
        {
            await scheduler.EnqueueAsync<ThrottledJob, ThrottledInput>(new ThrottledInput(i));
        }

        // Wait for all jobs to finish
        await concurrencyTracker.WaitForAllAsync(totalJobs, TimeSpan.FromSeconds(10));

        concurrencyTracker.PeakConcurrency.Should().BeLessOrEqualTo(
            maxConcurrent,
            $"[Throttle] limits concurrent executions to {maxConcurrent}");

        await host.StopAsync();
    }

    // ─── unthrottled job runs freely ─────────────────────────────────────────

    [Fact]
    public async Task UnthrottledJob_CanRunConcurrently()
    {
        const int totalJobs = 4;
        var tcs = new TaskCompletionSource<bool>();

        using var host = BuildHost(
            s => s.AddTransient(_ => new QuickSuccessJob(tcs)),
            workers: 4);

        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        for (var i = 0; i < totalJobs; i++)
        {
            await scheduler.EnqueueAsync<QuickSuccessJob, QuickInput>(new QuickInput());
        }

        // All 4 jobs should complete
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        var metrics = await storage.GetMetricsAsync();
        metrics.Succeeded.Should().BeGreaterOrEqualTo(1);

        await host.StopAsync();
    }
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

public sealed class ConcurrencyTracker
{
    private int _current;
    private int _peak;
    private int _completed;
    private readonly List<TaskCompletionSource<bool>> _waiters = [];

    public int PeakConcurrency => _peak;

    public void Enter()
    {
        var current = Interlocked.Increment(ref _current);
        int peak;
        do
        {
            peak = _peak;
            if (current <= peak)
            {
                break;
            }
        }
        while (Interlocked.CompareExchange(ref _peak, current, peak) != peak);
    }

    public void Exit(int totalExpected)
    {
        Interlocked.Decrement(ref _current);
        var done = Interlocked.Increment(ref _completed);
        if (done >= totalExpected)
        {
            lock (_waiters)
            {
                foreach (var w in _waiters)
                {
                    w.TrySetResult(true);
                }
            }
        }
    }

    public Task WaitForAllAsync(int count, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<bool>();
        lock (_waiters)
        {
            if (_completed >= count)
            {
                return Task.CompletedTask;
            }

            _waiters.Add(tcs);
        }

        return tcs.Task.WaitAsync(timeout);
    }
}

// ─── Stub jobs ────────────────────────────────────────────────────────────────

public record ThrottledInput(int Index);

[Throttle("throttle-test-resource", 2)]
public sealed class ThrottledJob(ConcurrencyTracker tracker) : IJob<ThrottledInput>
{
    public async Task ExecuteAsync(ThrottledInput input, CancellationToken cancellationToken)
    {
        tracker.Enter();
        try
        {
            await Task.Delay(50, cancellationToken);
        }
        finally
        {
            tracker.Exit(totalExpected: 6);
        }
    }
}
