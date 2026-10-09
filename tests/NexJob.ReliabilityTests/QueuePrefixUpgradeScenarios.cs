using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// The v5 to v6 transition of the default queue (#377, #402), on every database provider. A v5 node stores and polls
/// <c>default</c>; a v6 node stores in <c>{prefix}.default</c> and polls both. A v5 node is simulated by a node without a
/// prefix (<c>EntryAssembly = null</c>) and what v5 stored is written straight into the storage with the queue
/// <c>default</c>, because v6 offers no way to enqueue into the legacy queue. Every test class has its own database;
/// each test uses a prefix of its own.
/// </summary>
public abstract class QueuePrefixUpgradeScenarios : DistributedReliabilityTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(700);

    /// <summary>Gets the action that registers the storage under test.</summary>
    /// <returns>The registration action.</returns>
    protected abstract Action<IServiceCollection> Storage();

    [Fact]
    public async Task JobsStoredTheWayV5LeavesThem_RunOnceOnAV6Node_AndNewJobsGoToThePrefixedQueue()
    {
        // N1 (Positive): the legacy queue is drained and the new queue is used.
        var prefix = $"up{Guid.NewGuid():N}"[..12];
        var log = new ExecutionLog();
        JobId[] legacy;
        using (var seed = BuildHost(Storage(), s => Register(s, log), workers: 1))
        {
            // Never started: only its storage is used to leave data behind, in the shape v5 uses.
            var storage = seed.Services.GetRequiredService<IStorageProvider>();
            legacy = [await WriteLegacyAsync(storage), await WriteLegacyAsync(storage), await WriteLegacyAsync(storage)];
        }

        using var host = BuildHost(Storage(), s => Register(s, log), workers: 2, configureOptions: o => o.QueuePrefix = prefix);
        var fresh = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<StepJob>();

        await host.StartAsync();

        var all = legacy.Append(fresh).ToArray();
        (await WaitForAllSucceeded(host, all, Timeout)).Should().BeTrue("the legacy queue is drained and the new queue is used");
        await Task.Delay(Grace);
        foreach (var id in all)
        {
            log.Count($"done:{id.Value}").Should().Be(1, $"job {id.Value} runs exactly once");
        }

        var dashboard = host.Services.GetRequiredService<IDashboardStorage>();
        (await dashboard.GetJobByIdAsync(fresh))!.Queue.Should().Be($"{prefix}.default");
        foreach (var id in legacy)
        {
            (await dashboard.GetJobByIdAsync(id))!.Queue.Should().Be("default", "no row is renamed");
        }

        await host.StopAsync();
    }

    [Fact]
    public async Task V5ShapedNodeAndV6Node_ShareOneDatabase_EveryJobRunsOnce_AndAV5NodeNeverTakesAV6Job()
    {
        // N2 (Negative): the rolling upgrade, with both kinds of node alive at once.
        var prefix = $"up{Guid.NewGuid():N}"[..12];
        var logV5 = new ExecutionLog();
        var logV6 = new ExecutionLog();
        using var nodeV5 = BuildHost(Storage(), s => Register(s, logV5), workers: 2, configureOptions: o => o.EntryAssembly = null);
        using var nodeV6 = BuildHost(Storage(), s => Register(s, logV6), workers: 2, configureOptions: o => o.QueuePrefix = prefix);

        var storage = nodeV6.Services.GetRequiredService<IStorageProvider>();
        var fromV5 = new List<JobId>();
        for (var i = 0; i < 3; i++)
        {
            fromV5.Add(await WriteLegacyAsync(storage));
        }

        var scheduler = nodeV6.Services.GetRequiredService<IScheduler>();
        var fromV6 = new List<JobId>();
        for (var i = 0; i < 3; i++)
        {
            fromV6.Add(await scheduler.EnqueueAsync<StepJob>());
        }

        await nodeV5.StartAsync();
        await nodeV6.StartAsync();

        var all = fromV5.Concat(fromV6).ToArray();
        (await WaitForAllSucceeded(nodeV6, all, Timeout)).Should().BeTrue("both kinds of node can work on one database");
        await Task.Delay(Grace);
        foreach (var id in all)
        {
            (logV5.Count($"done:{id.Value}") + logV6.Count($"done:{id.Value}")).Should().Be(1, $"job {id.Value} runs exactly once");
        }

        foreach (var id in fromV6)
        {
            logV5.Count($"done:{id.Value}").Should().Be(0, "a v5 node does not poll the prefixed queue");
        }

        await nodeV5.StopAsync();
        await nodeV6.StopAsync();
    }

    [Fact]
    public async Task Rollback_JobsEnqueuedByV6StayInThePrefixedQueue_UntilAV6NodeReadsThem()
    {
        // N2/N3 (Rollback): what the documentation warns about.
        var prefix = $"up{Guid.NewGuid():N}"[..12];
        var log = new ExecutionLog();
        JobId stranded;
        using (var v6Producer = BuildHost(Storage(), s => Register(s, log), workers: 1, configureOptions: o => o.QueuePrefix = prefix))
        {
            stranded = await v6Producer.Services.GetRequiredService<IScheduler>().EnqueueAsync<StepJob>();
        }

        JobId sentinel;
        using (var v5Again = BuildHost(Storage(), s => Register(s, log), workers: 1, configureOptions: o => o.EntryAssembly = null))
        {
            sentinel = await WriteLegacyAsync(v5Again.Services.GetRequiredService<IStorageProvider>());
            await v5Again.StartAsync();

            (await WaitForAllSucceeded(v5Again, [sentinel], Timeout)).Should().BeTrue("the v5-shaped node is alive and polling");
            var job = await v5Again.Services.GetRequiredService<IDashboardStorage>().GetJobByIdAsync(stranded);
            job!.Status.Should().Be(JobStatus.Enqueued, "a v5 node never reads the prefixed queue");
            log.Count($"done:{stranded.Value}").Should().Be(0);
            await v5Again.StopAsync();
        }

        using var v6Again = BuildHost(Storage(), s => Register(s, log), workers: 1, configureOptions: o => o.QueuePrefix = prefix);
        await v6Again.StartAsync();

        (await WaitForAllSucceeded(v6Again, [stranded], Timeout)).Should().BeTrue("a v6 node with the same prefix reads it");
        await Task.Delay(Grace);
        log.Count($"done:{stranded.Value}").Should().Be(1);
        await v6Again.StopAsync();
    }

    private static async Task<JobId> WriteLegacyAsync(IStorageProvider storage)
    {
        var id = JobId.New();
        await storage.EnqueueAsync(new JobRecord
        {
            Id = id,
            JobType = typeof(StepJob).AssemblyQualifiedName!,
            InputType = typeof(NoInput).AssemblyQualifiedName!,
            InputJson = "{}",
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.Enqueued,
            CreatedAt = DateTimeOffset.UtcNow,
            MaxAttempts = 3,
        });
        return id;
    }

    private static void Register(IServiceCollection services, ExecutionLog log)
    {
        services.AddSingleton(log);
        services.AddTransient<StepJob>();
    }
}
