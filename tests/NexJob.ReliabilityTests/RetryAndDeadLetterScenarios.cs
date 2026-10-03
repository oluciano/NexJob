using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexJob;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Retry and dead-letter scenarios shared by every storage provider, one end-to-end case each.
/// Each test uses its own queue. A subclass only says how to register its storage.
/// </summary>
public abstract class RetryAndDeadLetterScenarios : DistributedReliabilityTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(500);

    /// <summary>Gets the action that registers the storage under test.</summary>
    /// <returns>The registration action.</returns>
    protected abstract Action<IServiceCollection> Storage();

    private static string NewQueue() => $"retry-{Guid.NewGuid():N}";

    [Fact]
    public async Task RetryExecutesAfterFailureAndSucceedsOnSecondAttempt()
    {
        var queue = NewQueue();
        var counter = new ExecutionCounter();
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<FailOnceThenSucceedJob>(sp => new FailOnceThenSucceedJob(counter.Increment, sp.GetRequiredService<ILogger<FailOnceThenSucceedJob>>(), sp.GetRequiredService<IJobContext>())),
            workers: 1,
            queues: [queue]);

        await host.StartAsync();

        var jobId = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<FailOnceThenSucceedJob>(queue: queue);

        var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, Timeout);

        job.Should().NotBeNull("the job should succeed after one retry");
        job!.Attempts.Should().Be(2, "one failure and one success");
        counter.Count.Should().Be(2);

        await host.StopAsync();
    }

    [Fact]
    public async Task DeadLetterHandlerInvokedExactlyOnceAfterMaxAttemptsExhausted_NoInput()
    {
        var queue = NewQueue();
        var recorder = new DeadLetterRecorder();
        var counter = new ExecutionCounter();
        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<AlwaysFailJob>(sp => new AlwaysFailJob(counter.Increment, sp.GetRequiredService<ILogger<AlwaysFailJob>>()));
                s.AddSingleton(recorder);
                s.AddTransient<IDeadLetterHandler<AlwaysFailJob>, RecordingDeadLetterHandler<AlwaysFailJob>>();
            },
            workers: 1,
            queues: [queue]);

        await host.StartAsync();

        var jobId = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<AlwaysFailJob>(queue: queue);

        (await WaitUntil(() => Task.FromResult(recorder.InvocationCount > 0), Timeout)).Should().BeTrue("the handler should be invoked");
        await Task.Delay(Grace);

        recorder.InvocationCount.Should().Be(1, "the handler runs once, not once per attempt");
        recorder.LastFailedJob!.Id.Should().Be(jobId);
        counter.Count.Should().Be(3, "the job runs once per attempt before dead-lettering");

        var job = await WaitForJobStatus(host, jobId, JobStatus.Failed, Timeout);
        job.Should().NotBeNull("the job ends in the Failed terminal state");

        await host.StopAsync();
    }

    [Fact]
    public async Task DeadLetterHandlerInvokedExactlyOnceAfterMaxAttemptsExhausted_WithInput()
    {
        var queue = NewQueue();
        var recorder = new DeadLetterRecorder();
        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<AlwaysFailJobWithInput>(sp => new AlwaysFailJobWithInput(() => { }, sp.GetRequiredService<ILogger<AlwaysFailJobWithInput>>()));
                s.AddSingleton(recorder);
                s.AddTransient<IDeadLetterHandler<AlwaysFailJobWithInput>, RecordingDeadLetterHandler<AlwaysFailJobWithInput>>();
            },
            workers: 1,
            queues: [queue]);

        await host.StartAsync();

        var jobId = await host.Services.GetRequiredService<IScheduler>()
            .EnqueueAsync<AlwaysFailJobWithInput, AlwaysFailInput>(new AlwaysFailInput("test"), queue: queue);

        (await WaitUntil(() => Task.FromResult(recorder.InvocationCount > 0), Timeout)).Should().BeTrue("the handler should be invoked");
        await Task.Delay(Grace);

        recorder.InvocationCount.Should().Be(1);
        recorder.LastFailedJob!.Id.Should().Be(jobId);

        await host.StopAsync();
    }

    [Fact]
    public async Task DeadLetterHandlerExceptionDoesNotCrashDispatcher()
    {
        var queue = NewQueue();
        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<AlwaysFailJob>(sp => new AlwaysFailJob(() => { }, sp.GetRequiredService<ILogger<AlwaysFailJob>>()));
                s.AddTransient<SuccessJob>(sp => new SuccessJob(() => { }, sp.GetRequiredService<ILogger<SuccessJob>>()));
                s.AddTransient<IDeadLetterHandler<AlwaysFailJob>, ThrowingDeadLetterHandler<AlwaysFailJob>>();
            },
            workers: 1,
            queues: [queue]);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var failingId = await scheduler.EnqueueAsync<AlwaysFailJob>(queue: queue);

        (await WaitForJobStatus(host, failingId, JobStatus.Failed, Timeout)).Should().NotBeNull("the job is persisted as Failed despite the handler exception");

        var okId = await scheduler.EnqueueAsync<SuccessJob>(queue: queue);
        (await WaitForJobStatus(host, okId, JobStatus.Succeeded, Timeout)).Should().NotBeNull("the dispatcher keeps processing after the handler threw");

        await host.StopAsync();
    }
}
