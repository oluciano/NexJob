using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// A job that cannot get its [Throttle] slot must not keep its worker slot indefinitely (#299).
/// </summary>
public sealed class ThrottleStarvationTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(8);

    private static IHost BuildHost(StarvationGate gate, int workers, int maxAttempts = 1)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = workers;
                    opt.MaxAttempts = maxAttempts;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                    opt.Queues = ["default", "other"];
                    opt.ThrottleMaxWait = TimeSpan.FromMilliseconds(200);
                });
                services.AddSingleton(gate);
                services.AddTransient<GatedThrottledJob>();
                services.AddTransient<OtherQueueJob>();
            })
            .Build();
    }

    // ─── N1: a saturated resource does not starve another queue ───────────────

    [Fact]
    public async Task SaturatedResource_DoesNotStarveJobsOfAnotherQueue()
    {
        var gate = new StarvationGate();
        using var host = BuildHost(gate, workers: 2);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        // A holds the only slot of the resource; B waits for it and, today, keeps the second worker.
        await scheduler.EnqueueAsync<GatedThrottledJob>();
        await gate.FirstEntered.WaitAsync(Window);
        await scheduler.EnqueueAsync<GatedThrottledJob>();
        var otherId = await scheduler.EnqueueAsync<OtherQueueJob>(queue: "other");

        var storage = host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        var ran = await WaitForStatus(storage, otherId, JobStatus.Succeeded, TimeSpan.FromSeconds(4));

        ran.Should().BeTrue("a job of another queue must run while a throttled resource is saturated");

        gate.Release();
        await host.StopAsync();
    }

    // ─── N2: once the slot is free the waiting job runs, and keeps its attempt ─

    [Fact]
    public async Task WaitingJob_RunsOnceTheSlotIsReleased_WithoutConsumingAnAttempt()
    {
        var gate = new StarvationGate();
        using var host = BuildHost(gate, workers: 2);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var firstId = await scheduler.EnqueueAsync<GatedThrottledJob>();
        await gate.FirstEntered.WaitAsync(Window);
        var waiterId = await scheduler.EnqueueAsync<GatedThrottledJob>();

        // Let the waiter exceed its wait and be returned to the queue at least once.
        await Task.Delay(600);
        gate.Release();

        var storage = host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        (await WaitForStatus(storage, firstId, JobStatus.Succeeded, Window)).Should().BeTrue();
        (await WaitForStatus(storage, waiterId, JobStatus.Succeeded, Window)).Should().BeTrue("the waiter runs once the slot is free");

        var waiter = await storage.GetJobByIdAsync(waiterId);
        waiter!.Attempts.Should().Be(1, "being returned to the queue for a throttle slot is not a failed attempt");

        await host.StopAsync();
    }

    // ─── N3: more waiters than workers neither deadlock nor consume attempts ──

    [Fact]
    public async Task MoreWaitersThanWorkers_DoNotDeadlockOrFail()
    {
        const int total = 5;
        var gate = new StarvationGate();
        using var host = BuildHost(gate, workers: 2, maxAttempts: 1);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var ids = new List<JobId> { await scheduler.EnqueueAsync<GatedThrottledJob>() };
        await gate.FirstEntered.WaitAsync(Window);
        for (var i = 1; i < total; i++)
        {
            ids.Add(await scheduler.EnqueueAsync<GatedThrottledJob>());
        }

        await Task.Delay(600);
        gate.Release();

        var storage = host.Services.GetRequiredService<NexJob.Storage.IStorageProvider>();
        foreach (var id in ids)
        {
            (await WaitForStatus(storage, id, JobStatus.Succeeded, TimeSpan.FromSeconds(20)))
                .Should().BeTrue("with MaxAttempts = 1 a job that lost an attempt waiting for the slot would fail");
        }

        await host.StopAsync();
    }

    private static async Task<bool> WaitForStatus(NexJob.Storage.IStorageProvider storage, JobId id, JobStatus status, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var job = await storage.GetJobByIdAsync(id);
            if (job?.Status == status)
            {
                return true;
            }

            await Task.Delay(25);
        }

        return false;
    }
}

/// <summary>Lets the test hold the first throttled job inside its slot until released.</summary>
public sealed class StarvationGate
{
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _firstEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task FirstEntered => _firstEntered.Task;

    public Task Released => _release.Task;

    public void Entered() => _firstEntered.TrySetResult();

    public void Release() => _release.TrySetResult();
}

[Throttle("starvation-resource", 1)]
public sealed class GatedThrottledJob(StarvationGate gate) : IJob
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        gate.Entered();
        await gate.Released.WaitAsync(cancellationToken);
    }
}

public sealed class OtherQueueJob : IJob
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
