using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexJob;
using Xunit;

namespace NexJob.ReliabilityTests.Distributed;

/// <summary>
/// Restart scenarios shared by every storage provider: what is persisted before a host stops must be
/// picked up, exactly once, by the next host. Each test uses its own queue so tests sharing one database
/// never see each other's jobs. A subclass only says how to register its storage.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public abstract class RecoveryScenarios : DistributedReliabilityTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(500);

    /// <summary>Gets the action that registers the storage under test.</summary>
    /// <returns>The registration action.</returns>
    protected abstract Action<IServiceCollection> Storage();

    [Fact]
    public async Task EnqueuedJobsSurviveHostRestartAndRunExactlyOnce_NoInput()
    {
        var queue = $"recovery-{Guid.NewGuid():N}";
        var counter = new ExecutionCounter();
        var jobIds = new List<JobId>();

        // Host 1 serves another queue, so it only persists the jobs and never runs them.
        using (var host1 = BuildHost(Storage(), s => s.AddTransient<SuccessJob>(sp => new SuccessJob(counter.Increment, sp.GetRequiredService<ILogger<SuccessJob>>())), workers: 1))
        {
            await host1.StartAsync();
            var scheduler = host1.Services.GetRequiredService<IScheduler>();
            for (var i = 0; i < 3; i++)
            {
                jobIds.Add(await scheduler.EnqueueAsync<SuccessJob>(queue: queue));
            }

            await host1.StopAsync();
        }

        counter.Count.Should().Be(0, "the first host does not serve the queue");

        using var host2 = BuildHost(Storage(), s => s.AddTransient<SuccessJob>(sp => new SuccessJob(counter.Increment, sp.GetRequiredService<ILogger<SuccessJob>>())), workers: 2, queues: [queue]);
        await host2.StartAsync();

        (await WaitForAllSucceeded(host2, jobIds, Timeout)).Should().BeTrue("the second host should run every persisted job");
        await Task.Delay(Grace);

        counter.Count.Should().Be(3, "each persisted job runs exactly once after the restart");

        await host2.StopAsync();
    }

    [Fact]
    public async Task EnqueuedJobsSurviveHostRestartAndRunExactlyOnce_WithInput()
    {
        var queue = $"recovery-{Guid.NewGuid():N}";
        var counter = new ExecutionCounter();
        var jobIds = new List<JobId>();

        using (var host1 = BuildHost(Storage(), s => s.AddTransient<SuccessJobWithInput>(sp => new SuccessJobWithInput(counter.Increment, sp.GetRequiredService<ILogger<SuccessJobWithInput>>())), workers: 1))
        {
            await host1.StartAsync();
            var scheduler = host1.Services.GetRequiredService<IScheduler>();
            for (var i = 0; i < 3; i++)
            {
                jobIds.Add(await scheduler.EnqueueAsync<SuccessJobWithInput, SuccessInput>(new SuccessInput($"recover-{i}"), queue: queue));
            }

            await host1.StopAsync();
        }

        using var host2 = BuildHost(Storage(), s => s.AddTransient<SuccessJobWithInput>(sp => new SuccessJobWithInput(counter.Increment, sp.GetRequiredService<ILogger<SuccessJobWithInput>>())), workers: 2, queues: [queue]);
        await host2.StartAsync();

        (await WaitForAllSucceeded(host2, jobIds, Timeout)).Should().BeTrue("the input must survive the restart");
        await Task.Delay(Grace);

        counter.Count.Should().Be(3);

        await host2.StopAsync();
    }

    [Fact]
    public async Task FailedJobStaysFailedAfterRestartAndIsNotRerun()
    {
        var queue = $"recovery-{Guid.NewGuid():N}";
        var counter = new ExecutionCounter();
        JobId jobId;

        using (var host1 = BuildHost(Storage(), s => s.AddTransient<AlwaysFailJob>(sp => new AlwaysFailJob(counter.Increment, sp.GetRequiredService<ILogger<AlwaysFailJob>>())), workers: 1, queues: [queue]))
        {
            await host1.StartAsync();
            jobId = await host1.Services.GetRequiredService<IScheduler>().EnqueueAsync<AlwaysFailJob>(queue: queue);

            var failed = await WaitForJobStatus(host1, jobId, JobStatus.Failed, Timeout);
            failed.Should().NotBeNull("the job should exhaust its attempts");

            await host1.StopAsync();
        }

        var executionsBeforeRestart = counter.Count;
        executionsBeforeRestart.Should().Be(3, "the job runs once per attempt");

        using var host2 = BuildHost(Storage(), s => s.AddTransient<AlwaysFailJob>(sp => new AlwaysFailJob(counter.Increment, sp.GetRequiredService<ILogger<AlwaysFailJob>>())), workers: 1, queues: [queue]);
        await host2.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));

        var job = await host2.Services.GetRequiredService<Storage.IStorageProvider>().GetJobByIdAsync(jobId);
        job!.Status.Should().Be(JobStatus.Failed, "a terminal state must survive the restart");
        counter.Count.Should().Be(executionsBeforeRestart, "a failed job is not run again");

        await host2.StopAsync();
    }
}
