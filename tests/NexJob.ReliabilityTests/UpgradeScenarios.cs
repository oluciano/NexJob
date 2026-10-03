using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob;
using NexJob.Storage;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Upgrade scenarios shared by every database provider. A storage that already holds data in the shape the previous
/// version left is upgraded by starting the current version on it. The previous shape is produced with the current code
/// and then moved one step back by removing exactly what this version added, so no old binaries are needed. Each
/// subclass says how to move back and what proves the upgrade happened. Every test class has its own database.
/// </summary>
public abstract class UpgradeScenarios : DistributedReliabilityTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(700);

    /// <summary>Gets the action that registers the storage under test.</summary>
    /// <returns>The registration action.</returns>
    protected abstract Action<IServiceCollection> Storage();

    /// <summary>Removes from the storage exactly what this version added, leaving what the previous version left.</summary>
    /// <returns>A task that completes when the storage is back in the previous shape.</returns>
    protected abstract Task MoveBackToPreviousVersionAsync();

    /// <summary>Asserts that what this version adds is present, which proves the upgrade ran.</summary>
    /// <returns>A task that completes when the check is done.</returns>
    protected abstract Task AssertUpgradedAsync();

    [Fact]
    public async Task PopulatedStorageFromThePreviousVersion_IsUpgraded_AndEverythingStillRunsOnce()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var runQueue = $"upgrade-run-{suffix}";
        var orphanQueue = $"upgrade-orphan-{suffix}";
        var historyQueue = $"upgrade-history-{suffix}";
        var log = new ExecutionLog();

        // ─── The storage as the previous version left it ─────────────────────
        JobId enqueuedA, enqueuedB, scheduled, parent, child, orphan, succeeded, failed;
        using (var seed = BuildHost(Storage(), s => Register(s, log), workers: 1, queues: [runQueue]))
        {
            // The seed host is never started: only its storage and scheduler are used to leave data behind.
            var scheduler = seed.Services.GetRequiredService<IScheduler>();
            var storage = seed.Services.GetRequiredService<IStorageProvider>();

            enqueuedA = await scheduler.EnqueueAsync<StepJob>(queue: runQueue);
            enqueuedB = await scheduler.EnqueueAsync<StepJob>(queue: runQueue);
            scheduled = await scheduler.ScheduleAsync<StepJob>(TimeSpan.FromMilliseconds(100), queue: runQueue);
            parent = await scheduler.EnqueueAsync<StepJob>(queue: runQueue);
            child = await scheduler.ContinueWithAsync<StepJob>(parent, queue: runQueue);

            // A job a crashed node left in Processing.
            orphan = await scheduler.EnqueueAsync<StepJob>(queue: orphanQueue);
            (await storage.FetchNextAsync([orphanQueue])).Should().NotBeNull();

            // Finished work that must stay exactly as it was.
            succeeded = await scheduler.EnqueueAsync<StepJob>(queue: historyQueue);
            var fetchedSucceeded = await storage.FetchNextAsync([historyQueue]);
            await storage.CommitJobResultAsync(fetchedSucceeded!.Id, new JobExecutionResult { Succeeded = true, Logs = [] });

            failed = await scheduler.EnqueueAsync<StepJob>(queue: historyQueue);
            var fetchedFailed = await storage.FetchNextAsync([historyQueue]);
            await storage.CommitJobResultAsync(
                fetchedFailed!.Id,
                new JobExecutionResult { Succeeded = false, Exception = new InvalidOperationException("old failure"), Logs = [] });
        }

        await Task.Delay(1500); // the orphan's heartbeat becomes older than the timeout used below
        await MoveBackToPreviousVersionAsync();

        // ─── Start this version on it ─────────────────────────────────────────
        var runnable = new[] { enqueuedA, enqueuedB, scheduled, parent, child, orphan };
        using (var host = BuildHost(
            Storage(),
            s => Register(s, log),
            workers: 3,
            queues: [runQueue, orphanQueue],
            heartbeatTimeout: TimeSpan.FromSeconds(1)))
        {
            await host.StartAsync();

            (await WaitForAllSucceeded(host, runnable, Timeout)).Should().BeTrue("everything left by the previous version should run");
            await Task.Delay(Grace);

            foreach (var id in runnable)
            {
                log.Count($"done:{id.Value}").Should().Be(1, $"job {id.Value} runs exactly once");
            }

            log.Entries.Should().ContainInOrder($"done:{parent.Value}", $"done:{child.Value}");

            var dashboard = host.Services.GetRequiredService<IDashboardStorage>();
            var oldSucceeded = await dashboard.GetJobByIdAsync(succeeded);
            var oldFailed = await dashboard.GetJobByIdAsync(failed);
            oldSucceeded!.Status.Should().Be(JobStatus.Succeeded, "finished history is untouched");
            oldFailed!.Status.Should().Be(JobStatus.Failed);
            log.Count($"done:{succeeded.Value}").Should().Be(0);
            log.Count($"done:{failed.Value}").Should().Be(0);

            foreach (var id in runnable.Append(succeeded).Append(failed))
            {
                (await dashboard.GetJobByIdAsync(id))!.ExpiresAt.Should().BeNull("a job written before deadlines were stored has none");
            }

            await host.StopAsync();
        }

        await AssertUpgradedAsync();

        // ─── Starting again on the upgraded storage changes nothing ──────────
        var executions = log.Entries.Count;
        using (var again = BuildHost(Storage(), s => Register(s, log), workers: 2, queues: [runQueue, orphanQueue], heartbeatTimeout: TimeSpan.FromSeconds(1)))
        {
            await again.StartAsync();
            await Task.Delay(TimeSpan.FromSeconds(2));
            await again.StopAsync();
        }

        log.Entries.Count.Should().Be(executions, "restarting on an already upgraded storage must not run anything again");
        await AssertUpgradedAsync();
    }

    [Fact]
    public async Task EmptyStorageFromThePreviousVersion_IsUpgradedAndAcceptsNewJobs()
    {
        // N3 (Boundary): nothing to migrate.
        var queue = $"upgrade-empty-{Guid.NewGuid():N}";
        var log = new ExecutionLog();
        using (var seed = BuildHost(Storage(), s => Register(s, log), workers: 1, queues: [queue]))
        {
            _ = seed.Services.GetRequiredService<IStorageProvider>(); // creates the schema
        }

        await MoveBackToPreviousVersionAsync();

        using var host = BuildHost(Storage(), s => Register(s, log), workers: 1, queues: [queue]);
        await host.StartAsync();
        var jobId = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<StepJob>(queue: queue);

        (await WaitForJobStatus(host, jobId, JobStatus.Succeeded, Timeout)).Should().NotBeNull();
        await host.StopAsync();
        await AssertUpgradedAsync();
    }

    private static void Register(IServiceCollection services, ExecutionLog log)
    {
        services.AddSingleton(log);
        services.AddTransient<StepJob>();
    }
}
