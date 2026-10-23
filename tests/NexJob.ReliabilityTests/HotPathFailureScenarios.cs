using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob;
using NexJob.Storage;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// The storage fails at three points of the job lifecycle (the batch fetch, the expiry of a job and the late release of a continuation), on a real provider,
/// and returns. After that the runtime has to recover without losing the job or running it twice. A subclass only says how to register its storage.
/// </summary>
public abstract class HotPathFailureScenarios : DistributedReliabilityTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);

    /// <summary>Gets the action that registers the storage under test.</summary>
    /// <returns>The registration action.</returns>
    protected abstract Action<IServiceCollection> Storage();

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    public async Task FetchBatchFailures_TheLoopStaysAlive_AndTheJobRunsOnce(int failures)
    {
        // N1 (1 failure, which is also the first call after start), N3 (3) and N2 (8: the storage stays down for a while).
        var plan = new FaultPlan(nameof(IJobStorage.FetchBatchAsync), failures);
        var counter = new ExecutionCounter();
        var queue = $"fetch-failure-{Guid.NewGuid():N}";
        using var host = BuildHost(
            s => Decorated(s, plan),
            s => s.AddTransient<SuccessJob>(sp => new SuccessJob(counter.Increment, sp.GetRequiredService<ILogger<SuccessJob>>())),
            queues: [queue]);
        await host.StartAsync();

        var id = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<SuccessJob>(queue: queue);

        (await WaitForJobStatus(host, id, JobStatus.Succeeded, Timeout)).Should().NotBeNull("the polling loop has to survive a failed fetch");
        counter.Count.Should().Be(1, "a fetch that failed claimed nothing, so the job is not run twice");
        plan.Injected.Should().Be(failures);
        await host.StopAsync();
    }

    [Fact]
    public async Task SetExpiredFailure_TheExpiredJobIsNeverRun_AndEndsExpired()
    {
        // N1: SetExpiredAsync fails once for a job whose deadline has passed. The exception leaves the executor before any
        // retry, so the job stays Processing until the orphan watcher gives it back; it must then expire, never run.
        var plan = new FaultPlan(nameof(IJobStorage.SetExpiredAsync), 1);
        var counter = new ExecutionCounter();
        var queue = $"expire-failure-{Guid.NewGuid():N}";
        using var host = BuildHost(
            s => Decorated(s, plan),
            s => s.AddTransient<SuccessJob>(sp => new SuccessJob(counter.Increment, sp.GetRequiredService<ILogger<SuccessJob>>())),
            queues: [queue],
            heartbeatTimeout: TimeSpan.FromSeconds(5),
            heartbeatInterval: TimeSpan.FromSeconds(1));
        await host.StartAsync();

        var id = await host.Services.GetRequiredService<IScheduler>()
            .EnqueueAsync<SuccessJob>(queue: queue, deadlineAfter: TimeSpan.FromMilliseconds(1));

        (await WaitForJobStatus(host, id, JobStatus.Expired, Timeout)).Should().NotBeNull("the job must end Expired once the storage works again");
        counter.Count.Should().Be(0, "an expired job never executes");
        plan.Injected.Should().Be(1);
        await host.StopAsync();
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public async Task EnqueueContinuationsFailure_OnTheLateContinuationPath_IsRetriedLikeAResultCommit(int failures, bool childRuns)
    {
        // The parent already succeeded when ContinueWithAsync registers the child, so the scheduler releases the child
        // itself with EnqueueContinuationsAsync. N1 (1 failure) and N3 (3: the last one the retries absorb): the child runs.
        // N2 (4: the budget is gone): the caller gets the exception and does not end up with a second child.
        var plan = new FaultPlan(nameof(IJobStorage.EnqueueContinuationsAsync), failures);
        var counter = new ExecutionCounter();
        var queue = $"continuation-failure-{Guid.NewGuid():N}";
        using var host = BuildHost(
            s => Decorated(s, plan),
            s => s.AddTransient<SuccessJob>(sp => new SuccessJob(counter.Increment, sp.GetRequiredService<ILogger<SuccessJob>>())),
            queues: [queue]);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var parent = await scheduler.EnqueueAsync<SuccessJob>(queue: queue);
        (await WaitForJobStatus(host, parent, JobStatus.Succeeded, Timeout)).Should().NotBeNull();

        var call = () => scheduler.ContinueWithAsync<SuccessJob>(parent, queue: queue);

        if (childRuns)
        {
            await call.Should().NotThrowAsync("the retries absorb the failure");
            (await WaitUntil(async () => await CountAsync(host, queue, JobStatus.Succeeded) == 2, Timeout)).Should().BeTrue("the child must run");
            counter.Count.Should().Be(2, "the parent and the child each run once");
        }
        else
        {
            await call.Should().ThrowAsync<InvalidOperationException>().WithMessage("*EnqueueContinuationsAsync*");
            (await CountAsync(host, queue, null)).Should().Be(2, "the parent and the one child; a failed call must not leave a second child");
            (await CountAsync(host, queue, JobStatus.AwaitingContinuation)).Should().Be(1, "the stored child is not released, which is the limit the docs state");
            counter.Count.Should().Be(1, "only the parent ran");
        }

        plan.Injected.Should().Be(failures);
        await host.StopAsync();
    }

    // The database is shared by the tests of a class, so a count has to be limited to the queue of the test.
    private static async Task<int> CountAsync(IHost host, string queue, JobStatus? status) =>
        (await host.Services.GetRequiredService<IStorageProvider>()
            .GetJobsAsync(filter: new JobFilter { Queue = queue, Status = status }, page: 1, pageSize: 100)).TotalCount;

    private void Decorated(IServiceCollection services, FaultPlan plan)
    {
        Storage()(services);
        FaultInjectingStorage<IJobStorage>.Decorate(services, plan);
        FaultInjectingStorage<IStorageProvider>.Decorate(services, plan);
    }
}
