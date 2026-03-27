using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Hangfire;
using Hangfire.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NexJob.Benchmarks;

/// <summary>
/// Measures end-to-end throughput: enqueue N jobs and wait for all to complete.
/// Compares NexJob vs Hangfire using in-memory storage and equal worker counts.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class ThroughputBenchmark
{
    private const int JobCount = 500;

    private IHost _nexJobHost = null!;
    private IScheduler _nexJobScheduler = null!;
    private BackgroundJobServer _hangfireServer = null!;
    private IBackgroundJobClient _hangfireClient = null!;

    // ── NexJob ────────────────────────────────────────────────────────────────

    /// <summary>Start the NexJob host before the benchmark iteration.</summary>
    [GlobalSetup(Target = nameof(NexJob_FireAndForget))]
    public async Task NexJobSetup()
    {
        _nexJobHost = Host.CreateDefaultBuilder()
            .ConfigureServices(s =>
            {
                s.AddNexJob(opt =>
                {
                    opt.Workers = 20;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(1);
                });
                s.AddTransient<NoOpJob>();
            })
            .Build();
        await _nexJobHost.StartAsync();
        _nexJobScheduler = _nexJobHost.Services.GetRequiredService<IScheduler>();
    }

    /// <summary>Stop the NexJob host after the benchmark iteration.</summary>
    [GlobalCleanup(Target = nameof(NexJob_FireAndForget))]
    public async Task NexJobCleanup() => await _nexJobHost.StopAsync();

    /// <summary>
    /// Enqueue <see cref="JobCount"/> jobs and wait for all to complete.
    /// </summary>
    [Benchmark(Baseline = true)]
    public async Task NexJob_FireAndForget()
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0;

        NoOpJob.SetCompletionCallback(() =>
        {
            if (Interlocked.Increment(ref completed) >= JobCount)
            {
                tcs.TrySetResult(true);
            }
        });

        for (var i = 0; i < JobCount; i++)
        {
            await _nexJobScheduler.EnqueueAsync<NoOpJob, NoOpInput>(new());
        }

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
        NoOpJob.SetCompletionCallback(null);
    }

    // ── Hangfire ──────────────────────────────────────────────────────────────

    /// <summary>Start the Hangfire server before the benchmark iteration.</summary>
    [GlobalSetup(Target = nameof(Hangfire_FireAndForget))]
    public void HangfireSetup()
    {
        GlobalConfiguration.Configuration.UseInMemoryStorage();
        _hangfireServer = new BackgroundJobServer(new BackgroundJobServerOptions { WorkerCount = 20 });
        _hangfireClient = new BackgroundJobClient();
    }

    /// <summary>Stop the Hangfire server after the benchmark iteration.</summary>
    [GlobalCleanup(Target = nameof(Hangfire_FireAndForget))]
    public void HangfireCleanup() => _hangfireServer.Dispose();

    /// <summary>Hangfire equivalent: enqueue 500 fire-and-forget jobs.</summary>
    [Benchmark]
    public void Hangfire_FireAndForget()
    {
        var completed = 0;
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        for (var i = 0; i < JobCount; i++)
        {
            _hangfireClient.Enqueue(() => HangfireNoOpJob.Execute(ref completed, tcs, JobCount));
        }

        tcs.Task.Wait(TimeSpan.FromSeconds(30));
    }
}
