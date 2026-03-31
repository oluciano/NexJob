using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexJob;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Reliability tests for wake-up channel latency and responsiveness.
/// Tests that local enqueue triggers immediate dispatcher wake-up
/// even with high polling intervals.
///
/// Trait: Reliability
/// </summary>
[Trait("Category", "Reliability")]
public sealed class WakeUpLatencyTests : ReliabilityTestBase
{
    [Fact]
    public async Task LocalEnqueueWakesDispatcherImmediatelyWithHighPollingInterval()
    {
        ResetTestState();

        using var host = BuildHost(s =>
        {
            s.AddTransient<SuccessJob>();
        }, workers: 1, pollingInterval: TimeSpan.FromSeconds(30)); // Very high polling interval

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var jobId = await scheduler.EnqueueAsync<SuccessJob>();

        // Wait for job to complete
        var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(5));
        stopwatch.Stop();

        job.Should().NotBeNull("job should succeed quickly despite high polling interval");

        // With wake-up channel, job should execute in <500ms
        // Without wake-up, would take ~30 seconds (polling interval)
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
            "wake-up channel should trigger immediate execution");

        await host.StopAsync();
    }

    [Fact]
    public async Task MultipleLocalEnqueuesResponsiveWithHighPolling()
    {
        ResetTestState();

        using var host = BuildHost(s =>
        {
            s.AddTransient<SuccessJob>();
        }, workers: 2, pollingInterval: TimeSpan.FromSeconds(20));

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();

        // Enqueue 5 jobs in quick succession
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var jobIds = new List<JobId>();

        for (int i = 0; i < 5; i++)
        {
            jobIds.Add(await scheduler.EnqueueAsync<SuccessJob>());
        }

        stopwatch.Stop();

        // Wait for all to complete
        await Task.Delay(2000);

        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();
        var succeededCount = 0;

        foreach (var jobId in jobIds)
        {
            var job = await storage.GetJobByIdAsync(jobId);
            if (job?.Status == JobStatus.Succeeded)
            {
                succeededCount++;
            }
        }

        succeededCount.Should().Be(5, "all jobs should execute quickly");

        await host.StopAsync();
    }

    [Fact]
    public async Task NoWakeUpNeededForDistributedScenario()
    {
        ResetTestState();

        // This test documents the architecture constraint:
        // Wake-up channel only works for LOCAL enqueue.
        // Distributed scenarios (different processes) fall back to polling.

        using var host = BuildHost(s =>
        {
            s.AddTransient<SuccessJob>();
        }, workers: 1, pollingInterval: TimeSpan.FromMilliseconds(500));

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();

        // Local enqueue should use wake-up
        var jobId = await scheduler.EnqueueAsync<SuccessJob>();

        var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(5));
        job.Should().NotBeNull("local enqueue should wake dispatcher immediately");

        await host.StopAsync();
    }

    [Fact]
    public async Task WakeUpChannelNonBlocking()
    {
        ResetTestState();

        using var host = BuildHost(s =>
        {
            s.AddTransient<SuccessJob>();
        }, workers: 1, pollingInterval: TimeSpan.FromSeconds(30));

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();

        // Enqueue should return immediately, not block on wake-up
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await scheduler.EnqueueAsync<SuccessJob>();
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100),
            "enqueue should return immediately without blocking on wake-up");

        await host.StopAsync();
    }
}
