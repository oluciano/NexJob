using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NexJob.Benchmarks;

/// <summary>
/// Measures the dispatch latency from when a job is enqueued to when it starts executing.
/// Demonstrates the low-latency wake-up channel mechanism.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class DispatchLatencyBenchmark
{
    private IHost _nexJobHost = null!;
    private IScheduler _nexJobScheduler = null!;

    /// <summary>
    /// Starts host with worker and immediate wake-up channel enabled.
    /// </summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _nexJobHost = Host.CreateDefaultBuilder()
            .ConfigureServices(s =>
            {
                s.AddNexJob(opt =>
                {
                    opt.Workers = 4;
                    opt.PollingInterval = TimeSpan.FromSeconds(30); // High polling interval to isolate wake-up channel signaling
                });
                s.AddTransient<NoOpJob>();
            })
            .Build();
        await _nexJobHost.StartAsync();
        _nexJobScheduler = _nexJobHost.Services.GetRequiredService<IScheduler>();
    }

    /// <summary>
    /// Stops the host.
    /// </summary>
    [GlobalCleanup]
    public async Task Cleanup() => await _nexJobHost.StopAsync();

    /// <summary>
    /// Enqueues a job and awaits its execution signal to measure wake-up dispatch latency.
    /// </summary>
    [Benchmark]
    public async Task<long> NexJob_WakeUpDispatchLatency()
    {
        var tcs = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sw = Stopwatch.StartNew();

        NoOpJob.SetCompletionCallback(() =>
        {
            sw.Stop();
            tcs.TrySetResult(sw.ElapsedTicks);
        });

        await _nexJobScheduler.EnqueueAsync<NoOpJob, NoOpInput>(new());
        var elapsedTicks = await tcs.Task;
        NoOpJob.SetCompletionCallback(null);
        return elapsedTicks;
    }
}
