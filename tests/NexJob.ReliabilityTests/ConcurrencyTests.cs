using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexJob;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Reliability tests for concurrency, duplicate prevention, and stress scenarios.
/// Tests that jobs execute exactly once despite concurrent workers,
/// and validates system behavior under load.
///
/// Trait: Reliability
/// </summary>
[Trait("Category", "Reliability")]
public sealed class ConcurrencyTests : ReliabilityTestBase
{
    [Fact]
    public async Task SingleJobNeverExecutesTwiceWithMultipleWorkers()
    {
        ResetTestState();

        using var host = BuildHost(s =>
        {
            s.AddTransient<TrackingJob>();
        }, workers: 3); // Multiple workers

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<TrackingJob>();

        // Wait for execution
        await Task.Delay(2000);

        TrackingJob.ExecutionCount.Should().Be(1, "job should execute exactly once despite multiple workers");

        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();
        var job = await storage.GetJobByIdAsync(jobId);
        job!.Status.Should().Be(JobStatus.Succeeded);

        await host.StopAsync();
    }

    [Fact]
    public async Task ConcurrentEnqueueOfMultipleJobsExecutesAll()
    {
        ResetTestState();

        using var host = BuildHost(s =>
        {
            s.AddTransient<SuccessJob>();
        }, workers: 2);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();

        // Enqueue 10 jobs concurrently
        var jobIds = new List<JobId>();
        var tasks = Enumerable.Range(0, 10)
            .Select(async _ =>
            {
                var id = await scheduler.EnqueueAsync<SuccessJob>();
                lock (jobIds)
                {
                    jobIds.Add(id);
                }
            });

        await Task.WhenAll(tasks);

        // Wait for all to execute
        await Task.Delay(3000);

        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();

        // All jobs should be succeeded
        var succeededCount = 0;
        foreach (var jobId in jobIds)
        {
            var job = await storage.GetJobByIdAsync(jobId);
            if (job?.Status == JobStatus.Succeeded)
            {
                succeededCount++;
            }
        }

        succeededCount.Should().Be(10, "all 10 jobs should have succeeded");

        await host.StopAsync();
    }

    [Fact]
    public async Task StressTest_LargeNumberOfJobsProcessedCorrectly()
    {
        ResetTestState();

        using var host = BuildHost(s =>
        {
            s.AddTransient<SuccessJob>();
        }, workers: 4);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();

        // Enqueue 50 jobs
        var jobIds = new List<JobId>();
        for (int i = 0; i < 50; i++)
        {
            var jobId = await scheduler.EnqueueAsync<SuccessJob>();
            jobIds.Add(jobId);
        }

        // Wait for processing
        var deadline = DateTime.UtcNow.AddSeconds(15);
        var allCompleted = false;

        while (DateTime.UtcNow < deadline && !allCompleted)
        {
            var succeededCount = 0;
            foreach (var jobId in jobIds)
            {
                var job = await storage.GetJobByIdAsync(jobId);
                if (job?.Status == JobStatus.Succeeded)
                {
                    succeededCount++;
                }
            }

            if (succeededCount == 50)
            {
                allCompleted = true;
            }
            else
            {
                await Task.Delay(200);
            }
        }

        allCompleted.Should().BeTrue("all 50 jobs should be completed");

        await host.StopAsync();
    }

    [Fact]
    public async Task NoDeadlocksUnderConcurrentWork()
    {
        ResetTestState();

        using var host = BuildHost(s =>
        {
            s.AddTransient<SuccessJob>();
        }, workers: 5);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();

        // Enqueue jobs continuously for a period
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        var enqueueCount = 0;

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                await scheduler.EnqueueAsync<SuccessJob>();
                enqueueCount++;
                await Task.Delay(10);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when timeout expires
        }

        // Wait for remaining jobs to process
        await Task.Delay(3000);

        // Verify system didn't deadlock by stopping cleanly
        await host.StopAsync();

        enqueueCount.Should().BeGreaterThan(10, "should have enqueued many jobs");
    }

    [Fact]
    public async Task MultipleWorkersHandleMixedSuccessAndFailure()
    {
        ResetTestState();

        using var host = BuildHost(s =>
        {
            s.AddTransient<SuccessJob>();
            s.AddTransient<AlwaysFailJob>();
        }, workers: 2);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();

        // Enqueue mix of success and fail jobs
        var successIds = new List<JobId>();
        var failIds = new List<JobId>();

        for (int i = 0; i < 5; i++)
        {
            successIds.Add(await scheduler.EnqueueAsync<SuccessJob>());
            failIds.Add(await scheduler.EnqueueAsync<AlwaysFailJob>());
        }

        // Wait for processing
        await Task.Delay(3000);

        // All success jobs should succeed
        var succeededCount = 0;
        foreach (var jobId in successIds)
        {
            var job = await storage.GetJobByIdAsync(jobId);
            if (job?.Status == JobStatus.Succeeded)
            {
                succeededCount++;
            }
        }

        succeededCount.Should().Be(5, "all success jobs should have succeeded");

        // All fail jobs should be in Failed state (after retries exhausted)
        var failedCount = 0;
        foreach (var jobId in failIds)
        {
            var job = await storage.GetJobByIdAsync(jobId);
            if (job?.Status == JobStatus.Failed && job.RetryAt == null)
            {
                failedCount++;
            }
        }

        failedCount.Should().Be(5, "all fail jobs should be in permanent failed state");

        await host.StopAsync();
    }
}
