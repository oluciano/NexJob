using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob;
using NexJob.Storage;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Several hosts at the same time on one database, each a separate NexJob node with its own dispatcher, as in a real
/// deployment. Shared by every database provider. Each test uses its own queue. A subclass only says how to register its
/// storage.
/// </summary>
public abstract class MultiNodeScenarios : DistributedReliabilityTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(800);

    /// <summary>Gets the action that registers the storage under test.</summary>
    /// <returns>The registration action.</returns>
    protected abstract Action<IServiceCollection> Storage();

    [Fact]
    public async Task ThreeNodesOnOneQueue_RunEveryJobExactlyOnce()
    {
        // N1 (Positive)
        const int total = 40;
        var queue = NewQueue();
        var log = new ExecutionLog();
        var nodes = await StartNodesAsync(3, queue, log);
        try
        {
            var scheduler = nodes[0].Services.GetRequiredService<IScheduler>();
            var ids = new List<JobId>();
            for (var i = 0; i < total; i++)
            {
                ids.Add(await scheduler.EnqueueAsync<NodeStepJob>(queue: queue));
            }

            (await WaitForAllSucceeded(nodes[0], ids, Timeout)).Should().BeTrue("every job should succeed");
            await Task.Delay(Grace);

            foreach (var id in ids)
            {
                log.Entries.Count(e => e.StartsWith($"done:{id.Value}@", StringComparison.Ordinal)).Should().Be(1, $"job {id.Value} runs exactly once across all nodes");
            }

            Participants(log).Should().HaveCountGreaterThan(1, "the work is shared, not served by a single node");
        }
        finally
        {
            await StopAndDisposeAsync(nodes);
        }
    }

    [Fact]
    public async Task NodeThatStopsAndNodeThatStartsLate_LoseNothing()
    {
        // N3 (Boundary): the set of nodes changes while work is going on.
        const int total = 30;
        var queue = NewQueue();
        var log = new ExecutionLog();
        var nodes = await StartNodesAsync(2, queue, log);
        IHost? late = null;
        try
        {
            var scheduler = nodes[0].Services.GetRequiredService<IScheduler>();
            var ids = new List<JobId>();
            for (var i = 0; i < total; i++)
            {
                ids.Add(await scheduler.EnqueueAsync<NodeStepJob>(queue: queue));
            }

            await WaitUntil(() => Task.FromResult(log.Entries.Count >= 5), Timeout);
            await nodes[1].StopAsync(); // graceful stop of one node in the middle of the run
            late = await StartNodeAsync("late", queue, log);

            (await WaitForAllSucceeded(nodes[0], ids, Timeout)).Should().BeTrue("the remaining and the late node finish the work");
            await Task.Delay(Grace);

            foreach (var id in ids)
            {
                log.Entries.Count(e => e.StartsWith($"done:{id.Value}@", StringComparison.Ordinal)).Should().Be(1);
            }
        }
        finally
        {
            late?.Dispose();
            await StopAndDisposeAsync(nodes);
        }
    }

    [Fact]
    public async Task TwoNodesWithTheSameRecurringJob_EnqueueOncePerOccurrence()
    {
        // N2 (Negative): the lock keeps two nodes from enqueuing the same occurrence.
        var recurringId = $"multi-node-{Guid.NewGuid():N}";
        var queue = NewQueue();
        var log = new ExecutionLog();
        var nodes = new List<IHost>();
        for (var i = 0; i < 2; i++)
        {
            var name = $"node-{i}";
            var host = BuildHost(
                Storage(),
                s => RegisterNode(s, log, name),
                workers: 1,
                pollingInterval: TimeSpan.FromMilliseconds(50),
                queues: [queue],
                configureOptions: opt => opt.AddRecurringJob<NodeStepJob>(recurringId, "* * * * * *", queue: queue));
            nodes.Add(host);
        }

        try
        {
            await Task.WhenAll(nodes.Select(h => h.StartAsync()));
            await Task.Delay(TimeSpan.FromSeconds(6));
            await Task.WhenAll(nodes.Select(h => h.StopAsync()));

            var storage = nodes[0].Services.GetRequiredService<IStorageProvider>();
            var page = await storage.GetJobsAsync(new JobFilter { RecurringJobId = recurringId }, page: 1, pageSize: 100);

            page.TotalCount.Should().BeGreaterThan(0, "the cron fires every second");
            foreach (var second in page.Items.GroupBy(j => j.CreatedAt.ToUnixTimeSeconds()))
            {
                second.Count().Should().Be(1, $"occurrence at second {second.Key} must be enqueued by only one node");
            }
        }
        finally
        {
            foreach (var host in nodes)
            {
                host.Dispose();
            }
        }
    }

    [Fact]
    public async Task TwoWatchers_DoNotRequeueTheSameOrphanTwice()
    {
        // N2 (Negative): both nodes see the orphan and both would give it back.
        var queue = NewQueue();
        var log = new ExecutionLog();
        JobId orphan;
        using (var seed = BuildHost(Storage(), s => RegisterNode(s, log, "seed"), workers: 1, queues: [queue]))
        {
            orphan = await seed.Services.GetRequiredService<IScheduler>().EnqueueAsync<NodeStepJob>(queue: queue);
            (await seed.Services.GetRequiredService<IStorageProvider>().FetchNextAsync([queue])).Should().NotBeNull();
            await Task.Delay(TimeSpan.FromMilliseconds(1500));
        }

        var nodes = new List<IHost>();
        for (var i = 0; i < 2; i++)
        {
            nodes.Add(BuildHost(Storage(), s => RegisterNode(s, log, $"node-{i}"), workers: 2, queues: [queue], heartbeatTimeout: TimeSpan.FromSeconds(1)));
        }

        try
        {
            await Task.WhenAll(nodes.Select(h => h.StartAsync()));

            (await WaitForJobStatus(nodes[0], orphan, JobStatus.Succeeded, Timeout)).Should().NotBeNull();
            await Task.Delay(TimeSpan.FromSeconds(2.5)); // another watcher cycle on both nodes

            log.Entries.Count(e => e.StartsWith($"done:{orphan.Value}@", StringComparison.Ordinal)).Should().Be(1, "two watchers must not both give the job back");
        }
        finally
        {
            await StopAndDisposeAsync(nodes);
        }
    }

    private static string NewQueue() => $"multinode-{Guid.NewGuid():N}";

    private static void RegisterNode(IServiceCollection services, ExecutionLog log, string node)
    {
        services.AddSingleton(log);
        services.AddSingleton(new NodeName(node));
        services.AddTransient<NodeStepJob>();
    }

    private static IReadOnlyCollection<string> Participants(ExecutionLog log) =>
        log.Entries.Where(e => e.Contains('@', StringComparison.Ordinal)).Select(e => e[(e.IndexOf('@', StringComparison.Ordinal) + 1)..]).Distinct(StringComparer.Ordinal).ToList();

    private async Task<IHost> StartNodeAsync(string name, string queue, ExecutionLog log)
    {
        var host = BuildHost(
            Storage(),
            s => RegisterNode(s, log, name),
            workers: 2,
            pollingInterval: TimeSpan.FromMilliseconds(50),
            queues: [queue]);
        await host.StartAsync();
        return host;
    }

    private async Task<List<IHost>> StartNodesAsync(int count, string queue, ExecutionLog log)
    {
        var nodes = new List<IHost>();
        for (var i = 0; i < count; i++)
        {
            nodes.Add(await StartNodeAsync($"node-{i}", queue, log));
        }

        return nodes;
    }

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
}
