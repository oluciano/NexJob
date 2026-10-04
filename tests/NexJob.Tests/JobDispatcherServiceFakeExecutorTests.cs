using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// The dispatcher's polling loop and worker pool, tested with a fake <see cref="IJobExecutor"/> instead of the real
/// execution pipeline (#167).
/// </summary>
public sealed class JobDispatcherServiceFakeExecutorTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(8);

    // ─── N1: a fetched job reaches the executor ───────────────────────────────

    [Fact]
    public async Task FetchedJob_IsHandedToTheExecutorExactlyOnce()
    {
        var executor = new FakeExecutor();
        using var host = BuildHost(executor, workers: 2);
        await host.StartAsync();

        var jobId = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<NoopJob>();
        await executor.WaitForStartedAsync(1);
        await Task.Delay(300);

        executor.Started.Should().ContainSingle().Which.Id.Should().Be(jobId);
        executor.Started[0].Status.Should().Be(JobStatus.Processing, "the dispatcher hands over jobs the storage already marked as processing");

        executor.ReleaseAll();
        await host.StopAsync();
    }

    // ─── N2: an executor that throws does not stop the loop ───────────────────

    [Fact]
    public async Task ExecutorThatThrows_DoesNotStopTheLoopOrTheOtherJobs()
    {
        var executor = new FakeExecutor { ThrowForFirstJob = true };
        using var host = BuildHost(executor, workers: 1);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        await scheduler.EnqueueAsync<NoopJob>();
        await executor.WaitForStartedAsync(1);
        await scheduler.EnqueueAsync<NoopJob>();

        await executor.WaitForStartedAsync(2);
        executor.Started.Should().HaveCount(2, "the slot of the throwing job was released and the loop kept polling");

        executor.ReleaseAll();
        await host.StopAsync();
    }

    // ─── N3: concurrency is bounded by Workers ────────────────────────────────

    [Fact]
    public async Task ConcurrentExecutions_NeverExceedTheNumberOfWorkers()
    {
        const int workers = 2;
        const int total = 6;
        var executor = new FakeExecutor();
        using var host = BuildHost(executor, workers: workers);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        for (var i = 0; i < total; i++)
        {
            await scheduler.EnqueueAsync<NoopJob>();
        }

        await executor.WaitForStartedAsync(workers);
        await Task.Delay(500);
        executor.Started.Should().HaveCount(workers, "no more jobs are fetched while every worker slot is busy");

        executor.ReleaseAll();
        await executor.WaitForStartedAsync(total);
        executor.PeakConcurrency.Should().BeLessOrEqualTo(workers);

        await host.StopAsync();
    }

    [Fact]
    public async Task Shutdown_WaitsForTheJobsThatAreStillRunning()
    {
        var executor = new FakeExecutor();
        using var host = BuildHost(executor, workers: 1, shutdownTimeout: TimeSpan.FromSeconds(10));
        await host.StartAsync();
        await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<NoopJob>();
        await executor.WaitForStartedAsync(1);

        var stopping = host.StopAsync();
        await Task.Delay(500);
        stopping.IsCompleted.Should().BeFalse("an in-flight job is given time to finish before the host stops");

        executor.ReleaseAll();
        await stopping.WaitAsync(Window);
    }

    private static IHost BuildHost(FakeExecutor executor, int workers, TimeSpan? shutdownTimeout = null)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = workers;
                    opt.MaxAttempts = 1;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                    if (shutdownTimeout is not null)
                    {
                        opt.ShutdownTimeout = shutdownTimeout.Value;
                    }
                });

                // The seam from #167: replaces the real execution pipeline.
                services.AddSingleton<IJobExecutor>(executor);
            })
            .Build();
    }

    public sealed class NoopJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeExecutor : IJobExecutor
    {
        private readonly List<JobRecord> _started = [];
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _concurrent;
        private int _peak;
        private int _calls;

        public bool ThrowForFirstJob { get; init; }

        public int PeakConcurrency => Volatile.Read(ref _peak);

        public IReadOnlyList<JobRecord> Started
        {
            get
            {
                lock (_started)
                {
                    return [.. _started];
                }
            }
        }

        public void ReleaseAll() => _release.TrySetResult();

        public async Task WaitForStartedAsync(int count)
        {
            var deadline = DateTime.UtcNow + Window;
            while (DateTime.UtcNow < deadline)
            {
                lock (_started)
                {
                    if (_started.Count >= count)
                    {
                        return;
                    }
                }

                await Task.Delay(20);
            }

            throw new TimeoutException($"Expected {count} job(s) to reach the executor, saw {Started.Count}.");
        }

        public async Task ExecuteJobAsync(JobRecord job, CancellationToken cancellationToken = default)
        {
            var now = Interlocked.Increment(ref _concurrent);
            int peak;
            do
            {
                peak = Volatile.Read(ref _peak);
            }
            while (now > peak && Interlocked.CompareExchange(ref _peak, now, peak) != peak);

            lock (_started)
            {
                _started.Add(job);
            }

            try
            {
                if (ThrowForFirstJob && Interlocked.Increment(ref _calls) == 1)
                {
                    throw new InvalidOperationException("executor failed");
                }

                await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _concurrent);
            }
        }
    }
}
