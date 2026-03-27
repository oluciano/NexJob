using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Hangfire;
using Hangfire.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NexJob.Benchmarks;

/// <summary>
/// Measures the latency of a single enqueue operation for NexJob vs Hangfire.
/// The polling interval is set high so only the storage write is timed.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class EnqueueLatencyBenchmark
{
    private IHost _nexJobHost = null!;
    private IScheduler _nexJobScheduler = null!;
    private IBackgroundJobClient _hangfireClient = null!;

    /// <summary>Set up both schedulers.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _nexJobHost = Host.CreateDefaultBuilder()
            .ConfigureServices(s =>
            {
                s.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.PollingInterval = TimeSpan.FromSeconds(60); // don't drain the queue
                });
                s.AddTransient<NoOpJob>();
            })
            .Build();
        await _nexJobHost.StartAsync();
        _nexJobScheduler = _nexJobHost.Services.GetRequiredService<IScheduler>();

        GlobalConfiguration.Configuration.UseInMemoryStorage();
        _hangfireClient = new BackgroundJobClient();
    }

    /// <summary>Stop the NexJob host after all iterations.</summary>
    [GlobalCleanup]
    public async Task Cleanup() => await _nexJobHost.StopAsync();

    /// <summary>Single NexJob enqueue — measures storage write + serialization.</summary>
    [Benchmark(Baseline = true)]
    public Task NexJob_SingleEnqueue()
        => _nexJobScheduler.EnqueueAsync<NoOpJob, NoOpInput>(new());

    /// <summary>Single Hangfire enqueue — equivalent fire-and-forget write.</summary>
    [Benchmark]
    public string Hangfire_SingleEnqueue()
        => _hangfireClient.Enqueue(() => Console.WriteLine("noop"));
}
