using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Continuation scenarios shared by every storage provider, through the real dispatcher: a child created with
/// <c>ContinueWithAsync</c> waits for its parent, runs once after the parent succeeds and never runs if the parent
/// fails. Each test uses its own queue. A subclass only says how to register its storage.
/// </summary>
public abstract class ContinuationScenarios : DistributedReliabilityTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(700);

    /// <summary>Gets the action that registers the storage under test.</summary>
    /// <returns>The registration action.</returns>
    protected abstract Action<IServiceCollection> Storage();

    [Fact]
    public async Task Child_WaitsForTheParent_ThenRunsAfterItSucceeds()
    {
        // N1 (Positive)
        using var rig = await StartAsync();
        var (host, log, gate, queue) = (rig.Host, rig.Log, rig.Gate, rig.Queue);
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var parent = await scheduler.EnqueueAsync<GatedParentJob>(queue: queue);
        var child = await scheduler.ContinueWithAsync<StepJob>(parent, queue: queue);
        (await WaitUntil(() => Task.FromResult(log.Count($"start:{parent.Value}") == 1), Timeout)).Should().BeTrue("the parent should start");

        await Task.Delay(Grace);
        log.Entries.Should().NotContain($"done:{child.Value}", "the child must wait while the parent is still running");
        (await StatusOf(host, child)).Should().Be(JobStatus.AwaitingContinuation);

        gate.OpenToSucceed();

        (await WaitForJobStatus(host, child, JobStatus.Succeeded, Timeout)).Should().NotBeNull("the child runs once the parent succeeded");
        log.Entries.Should().ContainInOrder($"done:{parent.Value}", $"done:{child.Value}");
        log.Count($"done:{child.Value}").Should().Be(1, "the child runs exactly once");

        await host.StopAsync();
    }

    [Fact]
    public async Task Child_StaysBlocked_WhenTheParentFails()
    {
        // N2 (Negative): the parent fails every attempt and is dead-lettered; the child must never run.
        using var rig = await StartAsync();
        var (host, log, gate, queue) = (rig.Host, rig.Log, rig.Gate, rig.Queue);
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var parent = await scheduler.EnqueueAsync<GatedParentJob>(queue: queue);
        var child = await scheduler.ContinueWithAsync<StepJob>(parent, queue: queue);
        gate.OpenToFail();

        (await WaitForJobStatus(host, parent, JobStatus.Failed, Timeout)).Should().NotBeNull("the parent exhausts its attempts");
        await Task.Delay(Grace);

        log.Entries.Should().NotContain($"done:{child.Value}", "a child never runs after a failed parent");
        (await StatusOf(host, child)).Should().Be(JobStatus.AwaitingContinuation);

        await host.StopAsync();
    }

    [Fact]
    public async Task Chain_RunsInOrder()
    {
        // N1: A -> B -> C
        using var rig = await StartAsync();
        var (host, log, gate, queue) = (rig.Host, rig.Log, rig.Gate, rig.Queue);
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var a = await scheduler.EnqueueAsync<GatedParentJob>(queue: queue);
        var b = await scheduler.ContinueWithAsync<StepJob>(a, queue: queue);
        var c = await scheduler.ContinueWithAsync<StepJob>(b, queue: queue);
        gate.OpenToSucceed();

        (await WaitForJobStatus(host, c, JobStatus.Succeeded, Timeout)).Should().NotBeNull("the last link of the chain should run");
        log.Entries.Where(e => e.StartsWith("done:", StringComparison.Ordinal))
            .Should().ContainInOrder($"done:{a.Value}", $"done:{b.Value}", $"done:{c.Value}");

        await host.StopAsync();
    }

    [Fact]
    public async Task FanOut_RunsEveryChildExactlyOnce()
    {
        // N1: one parent, several children
        const int children = 4;
        using var rig = await StartAsync();
        var (host, log, gate, queue) = (rig.Host, rig.Log, rig.Gate, rig.Queue);
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var parent = await scheduler.EnqueueAsync<GatedParentJob>(queue: queue);
        var ids = new List<JobId>();
        for (var i = 0; i < children; i++)
        {
            ids.Add(await scheduler.ContinueWithAsync<StepJob>(parent, queue: queue));
        }

        gate.OpenToSucceed();

        foreach (var id in ids)
        {
            (await WaitForJobStatus(host, id, JobStatus.Succeeded, Timeout)).Should().NotBeNull();
        }

        await Task.Delay(Grace);
        foreach (var id in ids)
        {
            log.Count($"done:{id.Value}").Should().Be(1, "no child runs twice");
        }

        await host.StopAsync();
    }

    [Fact]
    public async Task ContinuationOfAnUnknownParent_NeverRunsAndDoesNotCrashTheHost()
    {
        // N3 (Invalid input): a parent id that does not exist.
        using var rig = await StartAsync();
        var (host, log, queue) = (rig.Host, rig.Log, rig.Queue);
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var stopped = false;
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() => stopped = true);

        JobId? orphan = null;
        try
        {
            orphan = await scheduler.ContinueWithAsync<StepJob>(JobId.New(), queue: queue);
        }
        catch (Exception)
        {
            // Rejecting an unknown parent is as valid as accepting it; what matters is that nothing runs and nothing breaks.
        }

        await Task.Delay(Grace);

        log.Entries.Should().BeEmpty("a continuation without a real parent never runs");
        stopped.Should().BeFalse();
        if (orphan is { } id)
        {
            (await StatusOf(host, id)).Should().Be(JobStatus.AwaitingContinuation);
        }

        await host.StopAsync();
    }

    private async Task<Rig> StartAsync()
    {
        var queue = $"continuation-{Guid.NewGuid():N}";
        var log = new ExecutionLog();
        var gate = new ParentGate();
        var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddSingleton(log);
                s.AddSingleton(gate);
                s.AddTransient<GatedParentJob>();
                s.AddTransient<StepJob>();
            },
            workers: 2,
            queues: [queue]);
        await host.StartAsync();
        return new Rig(host, log, gate, queue);
    }

    private static async Task<JobStatus?> StatusOf(IHost host, JobId id)
    {
        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();
        return (await storage.GetJobByIdAsync(id))?.Status;
    }

    private sealed class Rig(IHost host, ExecutionLog log, ParentGate gate, string queue) : IDisposable
    {
        public IHost Host { get; } = host;

        public ExecutionLog Log { get; } = log;

        public ParentGate Gate { get; } = gate;

        public string Queue { get; } = queue;

        public void Dispose() => Host.Dispose();
    }
}
