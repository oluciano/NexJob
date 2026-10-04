using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexJob;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Concurrency scenarios shared by every storage provider: several workers on one storage must run each
/// job exactly once. A subclass only says how to register its storage.
/// </summary>
public abstract class ConcurrencyScenarios : DistributedReliabilityTestBase
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(500);

    /// <summary>Gets the action that registers the storage under test.</summary>
    /// <returns>The registration action.</returns>
    protected abstract Action<IServiceCollection> Storage();

    [Fact]
    public async Task SingleJobNeverExecutesTwiceWithMultipleWorkers_NoInput()
    {
        var counter = new ExecutionCounter();
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<TrackingJob>(sp => new TrackingJob(counter.Increment, sp.GetRequiredService<ILogger<TrackingJob>>())),
            workers: 3);

        await host.StartAsync();

        var jobId = await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<TrackingJob>();

        (await WaitForAllSucceeded(host, [jobId], Timeout)).Should().BeTrue("the job should succeed");
        await Task.Delay(Grace);

        counter.Count.Should().Be(1, "a job must never run twice, even with several workers polling");

        await host.StopAsync();
    }

    [Fact]
    public async Task ConcurrentEnqueueOfMultipleJobsExecutesAll_NoInput()
    {
        var counter = new ExecutionCounter();
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJob>(sp => new SuccessJob(counter.Increment, sp.GetRequiredService<ILogger<SuccessJob>>())),
            workers: 2);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobIds = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => scheduler.EnqueueAsync<SuccessJob>()));

        (await WaitForAllSucceeded(host, jobIds, Timeout)).Should().BeTrue("every job should succeed");
        await Task.Delay(Grace);

        counter.Count.Should().Be(5, "each job runs exactly once");

        await host.StopAsync();
    }

    [Fact]
    public async Task ConcurrentEnqueueOfMultipleJobsExecutesAll_WithInput()
    {
        var counter = new ExecutionCounter();
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJobWithInput>(sp => new SuccessJobWithInput(counter.Increment, sp.GetRequiredService<ILogger<SuccessJobWithInput>>())),
            workers: 2);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobIds = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(i => scheduler.EnqueueAsync<SuccessJobWithInput, SuccessInput>(new SuccessInput($"concurrent-{i}"))));

        (await WaitForAllSucceeded(host, jobIds, Timeout)).Should().BeTrue("every job should succeed");
        await Task.Delay(Grace);

        counter.Count.Should().Be(5, "each job runs exactly once");

        await host.StopAsync();
    }

    [Fact]
    public async Task HighThroughputJobsProcessCorrectly_NoInput()
    {
        const int total = 20;
        var counter = new ExecutionCounter();
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJob>(sp => new SuccessJob(counter.Increment, sp.GetRequiredService<ILogger<SuccessJob>>())),
            workers: 3);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobIds = new List<JobId>();
        for (var i = 0; i < total; i++)
        {
            jobIds.Add(await scheduler.EnqueueAsync<SuccessJob>());
        }

        (await WaitForAllSucceeded(host, jobIds, Timeout)).Should().BeTrue("every job should succeed");
        await Task.Delay(Grace);

        counter.Count.Should().Be(total, "no job is lost and none runs twice");

        await host.StopAsync();
    }

    [Fact]
    public async Task EmptyQueueWithMultipleWorkersRunsWithoutExecutingAnything()
    {
        var counter = new ExecutionCounter();
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJob>(sp => new SuccessJob(counter.Increment, sp.GetRequiredService<ILogger<SuccessJob>>())),
            workers: 3);

        await host.StartAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));
        await host.StopAsync();

        counter.Count.Should().Be(0, "an empty queue must not execute or throw");
    }
}
