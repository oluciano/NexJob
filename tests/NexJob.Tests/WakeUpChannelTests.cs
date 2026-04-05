using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Unit tests for <see cref="JobWakeUpChannel"/> covering bounded signaling,
/// non-blocking behavior, and integration with the dispatcher.
/// </summary>
public sealed class WakeUpChannelTests
{
    // ─── JobWakeUpChannel unit tests ──────────────────────────────────────────

    [Fact]
    public async Task Signal_AndWaitAsync_ReturnsTrue()
    {
        var channel = new JobWakeUpChannel();

        channel.Signal();
        var received = await channel.WaitAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        received.Should().BeTrue();
    }

    [Fact]
    public async Task WaitAsync_WithoutSignal_TimesOut()
    {
        var channel = new JobWakeUpChannel();

        var sw = Stopwatch.StartNew();
        var received = await channel.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);
        sw.Stop();

        received.Should().BeFalse();
        sw.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(45));
    }

    [Fact]
    public async Task MultipleSignals_CollapsedIntoOne()
    {
        var channel = new JobWakeUpChannel();

        // Send 5 rapid signals
        for (int i = 0; i < 5; i++)
        {
            channel.Signal();
        }

        // First wait succeeds
        var result1 = await channel.WaitAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);
        result1.Should().BeTrue();

        // Second wait times out (signal was collapsed)
        var result2 = await channel.WaitAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);
        result2.Should().BeFalse();
    }

    [Fact]
    public async Task WaitAsync_Respects_CancellationToken()
    {
        var channel = new JobWakeUpChannel();
        using var cts = new CancellationTokenSource();

        var task = channel.WaitAsync(TimeSpan.FromSeconds(10), cts.Token);

        // Cancel immediately
        await cts.CancelAsync();

        var received = await task;
        received.Should().BeFalse();
    }

    [Fact]
    public void Signal_IsNonBlocking()
    {
        var channel = new JobWakeUpChannel();

        // Send multiple signals rapidly — should not block
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 1000; i++)
        {
            channel.Signal();
        }

        sw.Stop();

        // Should complete very quickly (< 50ms for 1000 calls)
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(50));
    }

    // ─── dispatcher integration tests ─────────────────────────────────────────

    [Fact]
    public async Task LocalEnqueue_WakesDispatcherImmediately()
    {
        var tcs = new TaskCompletionSource<bool>();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.PollingInterval = TimeSpan.FromSeconds(5); // Long polling interval
                });
                services.AddTransient(_ => new QuickSuccessJob(tcs));
            })
            .Build();

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var sw = Stopwatch.StartNew();
        await scheduler.EnqueueAsync<QuickSuccessJob, QuickInput>(new());

        // Job should complete well before the polling interval
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1), "dispatcher should wake immediately");

        await host.StopAsync();
    }

    [Fact]
    public async Task PollingFallback_StillWorks_WhenNoSignal()
    {
        var tcs = new TaskCompletionSource<bool>();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(100);
                });
                services.AddTransient(_ => new QuickSuccessJob(tcs));
            })
            .Build();

        await host.StartAsync();

        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();

        // Enqueue directly to storage without using scheduler (bypasses signal)
        var jobId = JobId.New();
        await storage.EnqueueAsync(new JobRecord
        {
            Id = jobId,
            JobType = typeof(QuickSuccessJob).AssemblyQualifiedName!,
            InputType = typeof(QuickInput).AssemblyQualifiedName!,
            InputJson = "{}",
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.Enqueued,
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = 10,
        });

        // Job should still execute via polling fallback
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        tcs.Task.IsCompletedSuccessfully.Should().BeTrue("polling fallback should execute job");

        await host.StopAsync();
    }

    [Fact]
    public async Task MultipleEnqueues_AllExecute()
    {
        var tcs1 = new TaskCompletionSource<bool>();
        var tcs2 = new TaskCompletionSource<bool>();
        var tcs3 = new TaskCompletionSource<bool>();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 3;
                    opt.PollingInterval = TimeSpan.FromSeconds(5);
                });
                services.AddTransient<QuickSuccessJob>(_ => new QuickSuccessJob(tcs1));
                services.AddTransient<SecondSuccessJob>(_ => new SecondSuccessJob(tcs2));
                services.AddTransient<ThirdSuccessJob>(_ => new ThirdSuccessJob(tcs3));
            })
            .Build();

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();

        // Enqueue three jobs rapidly
        await scheduler.EnqueueAsync<QuickSuccessJob, QuickInput>(new());
        await scheduler.EnqueueAsync<SecondSuccessJob, QuickInput>(new());
        await scheduler.EnqueueAsync<ThirdSuccessJob, QuickInput>(new());

        // All should complete
        await Task.WhenAll(
            tcs1.Task.WaitAsync(TimeSpan.FromSeconds(2)),
            tcs2.Task.WaitAsync(TimeSpan.FromSeconds(2)),
            tcs3.Task.WaitAsync(TimeSpan.FromSeconds(2)));

        tcs1.Task.IsCompletedSuccessfully.Should().BeTrue();
        tcs2.Task.IsCompletedSuccessfully.Should().BeTrue();
        tcs3.Task.IsCompletedSuccessfully.Should().BeTrue();

        await host.StopAsync();
    }

    [Fact]
    public async Task ScheduledJob_DoesNotSignal()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(100);
                });
            })
            .Build();

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();

        // Schedule a job for far in the future
        await scheduler.ScheduleAsync<QuickSuccessJob, QuickInput>(
            new(),
            delay: TimeSpan.FromDays(1));

        // Should still be in Scheduled status
        await Task.Delay(150);
        var metrics = await storage.GetMetricsAsync();
        metrics.Scheduled.Should().Be(1);
        metrics.Processing.Should().Be(0, "scheduled job should not execute immediately");

        await host.StopAsync();
    }

    [Fact]
    public async Task NoInput_JobEnqueue_Signals()
    {
        var tcs = new TaskCompletionSource<bool>();

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.PollingInterval = TimeSpan.FromSeconds(5);
                });
                services.AddTransient(_ => new SimpleJob(tcs));
            })
            .Build();

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var sw = Stopwatch.StartNew();
        await scheduler.EnqueueAsync<SimpleJob>();

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));

        await host.StopAsync();
    }

    // ─── helper jobs ──────────────────────────────────────────────────────────

    private sealed class QuickInput
    {
    }

    private sealed class QuickSuccessJob : IJob<QuickInput>
    {
        private readonly TaskCompletionSource<bool> _tcs;

        public QuickSuccessJob(TaskCompletionSource<bool> tcs) => _tcs = tcs;

        public Task ExecuteAsync(QuickInput input, CancellationToken cancellationToken)
        {
            _tcs.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    private sealed class SecondSuccessJob : IJob<QuickInput>
    {
        private readonly TaskCompletionSource<bool> _tcs;

        public SecondSuccessJob(TaskCompletionSource<bool> tcs) => _tcs = tcs;

        public Task ExecuteAsync(QuickInput input, CancellationToken cancellationToken)
        {
            _tcs.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    private sealed class ThirdSuccessJob : IJob<QuickInput>
    {
        private readonly TaskCompletionSource<bool> _tcs;

        public ThirdSuccessJob(TaskCompletionSource<bool> tcs) => _tcs = tcs;

        public Task ExecuteAsync(QuickInput input, CancellationToken cancellationToken)
        {
            _tcs.TrySetResult(true);
            return Task.CompletedTask;
        }
    }

    private sealed class SimpleJob : IJob
    {
        private readonly TaskCompletionSource<bool> _tcs;

        public SimpleJob(TaskCompletionSource<bool> tcs) => _tcs = tcs;

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _tcs.TrySetResult(true);
            return Task.CompletedTask;
        }
    }
}
