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
    private string _payload = string.Empty;
    private NoOpInput _input = null!;

    /// <summary>
    /// Gets or sets the payload size in bytes to evaluate serialization overhead.
    /// </summary>
    [Params(0, 1024, 10240)]
    public int PayloadBytes { get; set; }

    /// <summary>Set up both schedulers and payload buffers.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        _payload = PayloadBytes > 0 ? new string('x', PayloadBytes) : string.Empty;
        _input = new NoOpInput { Payload = _payload, };

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
        => _nexJobScheduler.EnqueueAsync<NoOpJob, NoOpInput>(_input);

    /// <summary>Single Hangfire enqueue — equivalent fire-and-forget write.</summary>
    [Benchmark]
    public string Hangfire_SingleEnqueue()
        => _hangfireClient.Enqueue(() => HangfireNoOpJob.Execute(_payload));
}
