using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexJob.Storage;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// The database fails at the moment a finished job commits its result. The executor retries the commit three times
/// (four calls in all); past that the job stays Processing and the orphan watcher runs it again, as after a crash.
/// Shared by every database provider. A subclass only says how to register its storage.
/// </summary>
public abstract class CommitFailureScenarios : DistributedReliabilityTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);

    /// <summary>Gets the action that registers the storage under test.</summary>
    /// <returns>The registration action.</returns>
    protected abstract Action<IServiceCollection> Storage();

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 2)]
    public async Task CommitFailures_UpToTheRetryBudgetAreAbsorbed_PastItTheJobRunsAgain(int failures, int expectedRuns)
    {
        // N1 (0 failures), N3 (3 failures: the last one the retries absorb) and N2 (4 failures: the budget is gone).
        var plan = new FaultPlan(nameof(IJobStorage.CommitJobResultAsync), failures);
        var counter = new ExecutionCounter();
        var queue = $"commit-failure-{Guid.NewGuid():N}";
        using var host = BuildHost(
            s =>
            {
                Storage()(s);
                FaultInjectingStorage<IJobStorage>.Decorate(s, plan);
                FaultInjectingStorage<IStorageProvider>.Decorate(s, plan);
            },
            s => s.AddTransient<SuccessJob>(sp => new SuccessJob(counter.Increment, sp.GetRequiredService<ILogger<SuccessJob>>())),
            queues: [queue],
            // The commit retries wait about 2.6 s in all: a HeartbeatTimeout below that would give the job back while they run.
            heartbeatTimeout: TimeSpan.FromSeconds(5),
            heartbeatInterval: TimeSpan.FromSeconds(1));
        await host.StartAsync();

        var id = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<SuccessJob>(queue: queue);

        (await WaitForJobStatus(host, id, JobStatus.Succeeded, Timeout)).Should().NotBeNull("the job ends Succeeded either way");
        counter.Count.Should().Be(expectedRuns, "past the commit retries the job runs a second time: at-least-once, not exactly-once");
        plan.Injected.Should().Be(failures);
        await host.StopAsync();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public async Task FailureCommitFailures_UpToTheRetryBudgetAreAbsorbed_PastItTheOrphanWatcherDeadLetters(int failures, bool deadLetteredByWatcher)
    {
        // N1 (0 failures), N3 (3 failures: the last one the retries absorb) and N2 (4 failures: the budget is gone and the
        // job stays Processing until the orphan watcher closes it). In every case the dead-letter handler runs exactly once.
        var plan = new FaultPlan(nameof(IJobStorage.CommitJobResultAsync), failures);
        var recorder = new DeadLetterRecorder();
        var queue = $"commit-failure-{Guid.NewGuid():N}";
        using var host = BuildHost(
            s =>
            {
                Storage()(s);
                FaultInjectingStorage<IJobStorage>.Decorate(s, plan);
                FaultInjectingStorage<IStorageProvider>.Decorate(s, plan);
            },
            s =>
            {
                s.AddTransient<AlwaysFailJob>(sp => new AlwaysFailJob(() => { }, sp.GetRequiredService<ILogger<AlwaysFailJob>>()));
                s.AddSingleton(recorder);
                s.AddTransient<IDeadLetterHandler<AlwaysFailJob>, RecordingDeadLetterHandler<AlwaysFailJob>>();
            },
            queues: [queue],
            heartbeatTimeout: TimeSpan.FromSeconds(5),
            heartbeatInterval: TimeSpan.FromSeconds(1),
            maxAttempts: 1);
        await host.StartAsync();

        var id = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<AlwaysFailJob>(queue: queue);

        (await WaitForJobStatus(host, id, JobStatus.Failed, Timeout)).Should().NotBeNull("the job ends Failed either way");
        (await WaitUntil(() => Task.FromResult(recorder.InvocationCount > 0), Timeout)).Should().BeTrue("the dead-letter handler must run");
        recorder.InvocationCount.Should().Be(1, "the handler runs once, not once per commit attempt");
        if (deadLetteredByWatcher)
        {
            recorder.LastException.Should().BeOfType<OrphanedJobException>("the executor gave up and the watcher closed the job");
        }
        else
        {
            recorder.LastException.Should().BeOfType<InvalidOperationException>("the executor saved the failure and dead-lettered it with the job's own error");
        }

        plan.Injected.Should().Be(failures);
        await host.StopAsync();
    }
}
