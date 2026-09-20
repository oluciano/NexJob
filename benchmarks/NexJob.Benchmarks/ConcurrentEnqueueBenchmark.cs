using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Hangfire;
using Hangfire.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NexJob.Benchmarks;

/// <summary>
/// Measures enqueue throughput and lock contention across multiple concurrent threads.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class ConcurrentEnqueueBenchmark
{
    private const int TotalJobs = 1000;

    private IHost _nexJobHost = null!;
    private IScheduler _nexJobScheduler = null!;
    private IBackgroundJobClient _hangfireClient = null!;

    /// <summary>
    /// Gets or sets the degree of parallelism.
    /// </summary>
    [Params(10, 50)]
    public int ConcurrencyLevel { get; set; }

    /// <summary>
    /// Initializes hosts and storage for both systems.
    /// </summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _nexJobHost = Host.CreateDefaultBuilder()
            .ConfigureServices(s =>
            {
                s.AddNexJob(opt =>
                {
                    opt.Workers = 1;
                    opt.PollingInterval = TimeSpan.FromSeconds(60);
                });
                s.AddTransient<NoOpJob>();
            })
            .Build();
        await _nexJobHost.StartAsync();
        _nexJobScheduler = _nexJobHost.Services.GetRequiredService<IScheduler>();

        GlobalConfiguration.Configuration.UseInMemoryStorage();
        _hangfireClient = new BackgroundJobClient();
    }

    /// <summary>
    /// Shuts down the NexJob host.
    /// </summary>
    [GlobalCleanup]
    public async Task Cleanup() => await _nexJobHost.StopAsync();

    /// <summary>
    /// Enqueues jobs concurrently into NexJob across configured parallel workers.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void NexJob_ConcurrentEnqueue()
    {
        var input = new NoOpInput();
        Parallel.For(
            0,
            TotalJobs,
            new ParallelOptions { MaxDegreeOfParallelism = ConcurrencyLevel, },
            _ => _nexJobScheduler.EnqueueAsync<NoOpJob, NoOpInput>(input).GetAwaiter().GetResult());
    }

    /// <summary>
    /// Enqueues jobs concurrently into Hangfire across configured parallel workers.
    /// </summary>
    [Benchmark]
    public void Hangfire_ConcurrentEnqueue()
    {
        Parallel.For(
            0,
            TotalJobs,
            new ParallelOptions { MaxDegreeOfParallelism = ConcurrencyLevel, },
            _ => _hangfireClient.Enqueue(() => HangfireNoOpJob.Execute()));
    }
}
