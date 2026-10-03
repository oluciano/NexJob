using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// <see cref="IJobControlService"/> scenarios shared by every storage provider, through the real dispatcher: pausing a
/// queue stops execution and resuming releases it, a failed job can be requeued, a job can be deleted. Each test uses
/// its own queue, because pausing is stored per queue name. A subclass only says how to register its storage.
/// </summary>
public abstract class ControlServiceScenarios : DistributedReliabilityTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(700);

    /// <summary>Gets the action that registers the storage under test.</summary>
    /// <returns>The registration action.</returns>
    protected abstract Action<IServiceCollection> Storage();

    [Fact]
    public async Task PausedQueue_DoesNotRun_UntilItIsResumed()
    {
        // N1 (Positive)
        var queue = NewQueue();
        var counter = new ExecutionCounter();
        using var host = BuildHost(Storage(), Jobs(counter), workers: 2, queues: [queue]);
        var control = await PauseThenStartAsync(host, queue);
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var jobId = await scheduler.EnqueueAsync<SuccessJob>(queue: queue);
        await Task.Delay(Grace);

        counter.Count.Should().Be(0, "a paused queue must not run anything");
        (await StatusOf(host, jobId)).Should().Be(JobStatus.Enqueued);

        await control.ResumeQueueAsync(queue);

        (await WaitForJobStatus(host, jobId, JobStatus.Succeeded, Timeout)).Should().NotBeNull("resuming releases the waiting job");
        counter.Count.Should().Be(1);

        await host.StopAsync();
    }

    [Fact]
    public async Task PauseAndResume_AreIdempotent_AndSafeOnQueuesThatWereNeverPaused()
    {
        // N3 (Boundary)
        var queue = NewQueue();
        var counter = new ExecutionCounter();
        using var host = BuildHost(Storage(), Jobs(counter), workers: 1, queues: [queue]);
        await host.StartAsync();
        var control = host.Services.GetRequiredService<IJobControlService>();

        Func<Task> act = async () =>
        {
            await control.PauseQueueAsync(queue);
            await control.PauseQueueAsync(queue);
            await control.ResumeQueueAsync(NewQueue());
            await control.ResumeQueueAsync(queue);
            await control.ResumeQueueAsync(queue);
        };

        await act.Should().NotThrowAsync();

        var jobId = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<SuccessJob>(queue: queue);
        (await WaitForJobStatus(host, jobId, JobStatus.Succeeded, Timeout)).Should().NotBeNull("the queue ends up resumed");

        await host.StopAsync();
    }

    [Fact]
    public async Task RequeuedFailedJob_RunsAgain_WithFreshAttempts()
    {
        // N1: a dead-lettered job goes back to the queue and gets a full set of attempts.
        var queue = NewQueue();
        var counter = new ExecutionCounter();
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<AlwaysFailJob>(sp => new AlwaysFailJob(counter.Increment, sp.GetRequiredService<ILogger<AlwaysFailJob>>())),
            workers: 1,
            queues: [queue]);
        await host.StartAsync();
        var jobId = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<AlwaysFailJob>(queue: queue);

        (await WaitForJobStatus(host, jobId, JobStatus.Failed, Timeout)).Should().NotBeNull("it exhausts its attempts");
        counter.Count.Should().Be(3);

        await host.Services.GetRequiredService<IJobControlService>().RequeueJobAsync(jobId);

        (await WaitUntil(() => Task.FromResult(counter.Count == 6), Timeout)).Should().BeTrue("a requeued job gets all its attempts again");
        (await WaitForJobStatus(host, jobId, JobStatus.Failed, Timeout)).Should().NotBeNull("it fails again for the same reason");
        counter.Count.Should().Be(6);

        await host.StopAsync();
    }

    [Fact]
    public async Task DeletedJob_IsGone_AndNeverRuns()
    {
        // N1
        var queue = NewQueue();
        var counter = new ExecutionCounter();
        using var host = BuildHost(Storage(), Jobs(counter), workers: 1, queues: [queue]);
        var control = await PauseThenStartAsync(host, queue);

        var jobId = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<SuccessJob>(queue: queue);
        await control.DeleteJobAsync(jobId);
        await control.ResumeQueueAsync(queue);
        await Task.Delay(Grace);

        (await StatusOf(host, jobId)).Should().BeNull("a deleted job is no longer in storage");
        counter.Count.Should().Be(0, "a deleted job never runs");

        await host.StopAsync();
    }

    [Fact]
    public async Task UnknownJobId_IsIgnoredByRequeueAndDelete()
    {
        // N3 (Invalid input)
        using var host = BuildHost(Storage(), Jobs(new ExecutionCounter()), workers: 1, queues: [NewQueue()]);
        await host.StartAsync();
        var control = host.Services.GetRequiredService<IJobControlService>();

        Func<Task> act = async () =>
        {
            await control.RequeueJobAsync(JobId.New());
            await control.DeleteJobAsync(JobId.New());
        };

        await act.Should().NotThrowAsync();
        await host.StopAsync();
    }

    // The dispatcher notices a pause on its next poll, so pausing a running host and enqueuing right away can race with a
    // cycle already in flight. Pausing before the dispatcher starts leaves no such window.
    private static async Task<IJobControlService> PauseThenStartAsync(IHost host, string queue)
    {
        _ = host.Services.GetRequiredService<Storage.IStorageProvider>(); // creates the provider and its schema
        var control = host.Services.GetRequiredService<IJobControlService>();
        await control.PauseQueueAsync(queue);
        await host.StartAsync();
        return control;
    }

    private static string NewQueue() => $"control-{Guid.NewGuid():N}";

    private static Action<IServiceCollection> Jobs(ExecutionCounter counter) =>
        s => s.AddTransient<SuccessJob>(sp => new SuccessJob(counter.Increment, sp.GetRequiredService<ILogger<SuccessJob>>()));

    private static async Task<JobStatus?> StatusOf(IHost host, JobId id)
    {
        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();
        return (await storage.GetJobByIdAsync(id))?.Status;
    }
}
