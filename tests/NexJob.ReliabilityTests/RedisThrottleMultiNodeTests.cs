using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Redis;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// <c>[Throttle]</c> across two nodes on one Redis: per node by default, cluster-wide with the distributed throttle.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class RedisThrottleMultiNodeTests : DistributedReliabilityTestBase, IClassFixture<RedisReliabilityFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);
    private readonly RedisReliabilityFixture _fixture;

    public RedisThrottleMultiNodeTests(RedisReliabilityFixture fixture)
        => _fixture = fixture;

    [Fact]
    public async Task WithoutTheDistributedThrottle_TheLimitAppliesToEachNode_SoTwoNodesRunTwoAtOnce()
    {
        // N2 (Negative): the documented per-process scope. maxConcurrent x nodes is the effective limit.
        var probe = new ConcurrencyProbe();
        var queue = NewQueue();
        var nodes = await StartNodesAsync(probe, queue, distributed: false);
        try
        {
            var scheduler = nodes[0].Services.GetRequiredService<IScheduler>();
            var ids = new List<JobId>();
            for (var i = 0; i < 8; i++)
            {
                ids.Add(await scheduler.EnqueueAsync<PerNodeThrottledJob>(queue: queue));
            }

            (await WaitForAllSucceeded(nodes[0], ids, Timeout)).Should().BeTrue("every job should succeed");

            probe.Peak.Should().Be(2, "[Throttle(maxConcurrent: 1)] is held by each node's own semaphore");
        }
        finally
        {
            await StopAndDisposeAsync(nodes);
        }
    }

    [Fact]
    public async Task WithTheDistributedThrottle_TheLimitAppliesToTheWholeCluster()
    {
        // N1 (Positive)
        var probe = new ConcurrencyProbe();
        var queue = NewQueue();
        var nodes = await StartNodesAsync(probe, queue, distributed: true);
        try
        {
            var scheduler = nodes[0].Services.GetRequiredService<IScheduler>();
            var ids = new List<JobId>();
            for (var i = 0; i < 8; i++)
            {
                ids.Add(await scheduler.EnqueueAsync<ClusterThrottledJob>(queue: queue));
            }

            (await WaitForAllSucceeded(nodes[0], ids, Timeout)).Should().BeTrue("every job should still succeed, one after the other");

            probe.Peak.Should().Be(1, "the slot is shared by both nodes");
        }
        finally
        {
            await StopAndDisposeAsync(nodes);
        }
    }

    private static string NewQueue() => $"throttle-multinode-{Guid.NewGuid():N}";

    private static async Task StopAndDisposeAsync(IEnumerable<IHost> nodes)
    {
        foreach (var node in nodes)
        {
            try
            {
                await node.StopAsync();
            }
            finally
            {
                node.Dispose();
            }
        }
    }

    private async Task<List<IHost>> StartNodesAsync(ConcurrencyProbe probe, string queue, bool distributed)
    {
        var nodes = new List<IHost>();
        for (var i = 0; i < 2; i++)
        {
            var host = BuildHost(
                s =>
                {
                    s.AddNexJobRedis(_fixture.ConnectionString);
                    if (distributed)
                    {
                        s.AddNexJobDistributedThrottle();
                    }
                },
                s =>
                {
                    s.AddSingleton(probe);
                    s.AddTransient<PerNodeThrottledJob>();
                    s.AddTransient<ClusterThrottledJob>();
                },
                workers: 3,
                pollingInterval: TimeSpan.FromMilliseconds(50),
                queues: [queue]);
            await host.StartAsync();
            nodes.Add(host);
        }

        return nodes;
    }

    private sealed class ConcurrencyProbe
    {
        private int _current;
        private int _peak;

        public int Peak => Volatile.Read(ref _peak);

        public async Task HoldAsync(CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref _current);
            int seen;
            do
            {
                seen = Volatile.Read(ref _peak);
            }
            while (now > seen && Interlocked.CompareExchange(ref _peak, now, seen) != seen);

            await Task.Delay(600, cancellationToken);
            Interlocked.Decrement(ref _current);
        }
    }

    [Throttle("per-node-resource", maxConcurrent: 1)]
    private sealed class PerNodeThrottledJob(ConcurrencyProbe probe) : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => probe.HoldAsync(cancellationToken);
    }

    [Throttle("cluster-resource", maxConcurrent: 1)]
    private sealed class ClusterThrottledJob(ConcurrencyProbe probe) : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => probe.HoldAsync(cancellationToken);
    }
}
