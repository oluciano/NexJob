using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob;
using Xunit;
using Xunit.Abstractions;

namespace NexJob.ReliabilityTests;

/// <summary>
/// How many connections the database server sees while NexJob works. A running job holds no connection, so a node needs
/// about one per worker plus a few for its background services; a regression that opens a connection per job, or a pool per
/// component, shows up as a number far above that. The server is asked directly (system views), not the client library.
/// </summary>
public abstract class ConnectionCountScenarios : DistributedReliabilityTestBase
{
    private const int Slack = 15; // dispatcher, heartbeat, scheduler, retention, the commit in flight, the measuring connection
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly ITestOutputHelper _output;

    protected ConnectionCountScenarios(ITestOutputHelper output) => _output = output;

    /// <summary>Gets the action that registers the storage under test.</summary>
    /// <returns>The registration action.</returns>
    protected abstract Action<IServiceCollection> Storage();

    /// <summary>
    /// Closes the idle connections that earlier tests left in the client library's pool, so that a measurement does not start
    /// with connections the nodes under test would silently reuse.
    /// </summary>
    protected virtual void ResetClientPools()
    {
    }

    /// <summary>Asks the database server how many client connections it has open right now, the caller's own included.</summary>
    /// <returns>The number of connections.</returns>
    protected abstract Task<int> CountServerConnectionsAsync();

    [Fact]
    public async Task OneNode_UsesAboutOneConnectionPerWorker_UnderLoad()
    {
        const int workers = 10;
        const int jobs = 150;
        var queue = $"conn-{Guid.NewGuid():N}";
        ResetClientPools();
        var baseline = await CountServerConnectionsAsync();
        using var host = BuildHost(Storage(), s => Register(s), workers: workers, queues: [queue]);
        await host.StartAsync();

        var ids = await EnqueueAsync(host, queue, jobs);
        var peak = await PeakWhileAsync(() => WaitForAllSucceeded(host, ids, Timeout), baseline);

        _output.WriteLine($"{GetType().Name} 1 node: baseline={baseline} workers={workers} peak above baseline={peak}");
        (await WaitForAllSucceeded(host, ids, Timeout)).Should().BeTrue();
        peak.Should().BeLessOrEqualTo(workers + Slack, "a node needs about one connection per worker, not one per job");

        await host.StopAsync();
    }

    [Fact]
    public async Task ThreeNodes_UseAboutTheSumOfTheirWorkers_NotThreeFullPools()
    {
        const int nodes = 3;
        const int workers = 5;
        const int jobs = 90;
        var queue = $"conn-{Guid.NewGuid():N}";
        ResetClientPools();
        var baseline = await CountServerConnectionsAsync();
        var hosts = new List<IHost>();
        try
        {
            for (var i = 0; i < nodes; i++)
            {
                var host = BuildHost(Storage(), s => Register(s), workers: workers, queues: [queue]);
                await host.StartAsync();
                hosts.Add(host);
            }

            var ids = await EnqueueAsync(hosts[0], queue, jobs);
            var peak = await PeakWhileAsync(() => WaitForAllSucceeded(hosts[0], ids, Timeout), baseline);

            _output.WriteLine($"{GetType().Name} {nodes} nodes: baseline={baseline} workers={workers} each, peak above baseline={peak}");
            peak.Should().BeLessOrEqualTo(nodes * (workers + Slack), "the nodes add up their workers; they do not each open a full default pool");
        }
        finally
        {
            foreach (var host in hosts)
            {
                await host.StopAsync();
                host.Dispose();
            }
        }
    }

    private static void Register(IServiceCollection services) =>
        services.AddTransient<SuccessJob>(sp => new SuccessJob(() => { }, sp.GetRequiredService<ILogger<SuccessJob>>()));

    private static async Task<List<JobId>> EnqueueAsync(IHost host, string queue, int count)
    {
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var ids = new List<JobId>();
        for (var i = 0; i < count; i++)
        {
            ids.Add(await scheduler.EnqueueAsync<SuccessJob>(queue: queue));
        }

        return ids;
    }

    // Samples the server while the work runs and returns the highest count above the baseline.
    private async Task<int> PeakWhileAsync(Func<Task<bool>> work, int baseline)
    {
        var peak = 0;
        var running = work();
        while (!running.IsCompleted)
        {
            peak = Math.Max(peak, (await CountServerConnectionsAsync()) - baseline);
            await Task.Delay(25);
        }

        await running;
        return Math.Max(peak, (await CountServerConnectionsAsync()) - baseline);
    }
}
