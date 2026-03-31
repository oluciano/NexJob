using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexJob;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Reliability tests for concurrent enqueue stress scenarios.
/// Tests system behavior under high concurrent load with proper
/// synchronization and resource management.
///
/// Trait: Reliability
/// </summary>
[Trait("Category", "Reliability")]
public sealed class ConcurrentStressTests : ReliabilityTestBase
{
    [Fact]
    public async Task ConcurrentEnqueueOf100JobsAllExecute()
    {
        ResetTestState();

        using var host = BuildHost(s =>
        {
            s.AddTransient<SuccessJob>();
        }, workers: 4);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();

        // Enqueue 100 jobs concurrently
        var jobIds = new List<JobId>(100);
        var lockObj = new object();

        var tasks = Enumerable.Range(0, 100)
            .Select(async _ =>
            {
                var jobId = await scheduler.EnqueueAsync<SuccessJob>();
                lock (lockObj)
                {
                    jobIds.Add(jobId);
                }
            });

        await Task.WhenAll(tasks);

        // Wait for all to execute
        var deadline = DateTime.UtcNow.AddSeconds(20);
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

            if (succeededCount == 100)
            {
                allCompleted = true;
            }
            else
            {
                await Task.Delay(500);
            }
        }

        allCompleted.Should().BeTrue("all 100 jobs should be executed");

        await host.StopAsync();
    }

    [Fact]
    public async Task RapidEnqueueAndExecuteCycleWithoutDeadlock()
    {
        ResetTestState();

        using var host = BuildHost(s =>
        {
            s.AddTransient<SuccessJob>();
        }, workers: 3);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();

        // Rapidly enqueue and allow execution for 3 seconds
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(3));
        var enqueueCount = 0;

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                await scheduler.EnqueueAsync<SuccessJob>();
                enqueueCount++;

                // Small delay between enqueues
                await Task.Delay(5, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // System should still be responsive after stress
        var testJobId = await scheduler.EnqueueAsync<SuccessJob>();

        var job = await WaitForJobStatus(host, testJobId, JobStatus.Succeeded, TimeSpan.FromSeconds(5));
        job.Should().NotBeNull("system should remain responsive after concurrent stress");

        enqueueCount.Should().BeGreaterThan(50, "should have enqueued many jobs during stress test");

        await host.StopAsync();
    }

    [Fact]
    public async Task ConcurrentEnqueueWithMixedJobTypes()
    {
        ResetTestState();

        using var host = BuildHost(s =>
        {
            s.AddTransient<SuccessJob>();
            s.AddTransient<AlwaysFailJob>();
            s.AddTransient<FailOnceThenSucceedJob>();
        }, workers: 3);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();

        // Enqueue different job types concurrently
        var successIds = new List<JobId>();
        var failIds = new List<JobId>();
        var retryIds = new List<JobId>();
        var lockObj = new object();

        var tasks = Enumerable.Range(0, 30)
            .Select(async i =>
            {
                if (i % 3 == 0)
                {
                    var jobId = await scheduler.EnqueueAsync<SuccessJob>();
                    lock (lockObj) successIds.Add(jobId);
                }
                else if (i % 3 == 1)
                {
                    var jobId = await scheduler.EnqueueAsync<AlwaysFailJob>();
                    lock (lockObj) failIds.Add(jobId);
                }
                else
                {
                    var jobId = await scheduler.EnqueueAsync<FailOnceThenSucceedJob>();
                    lock (lockObj) retryIds.Add(jobId);
                }
            });

        await Task.WhenAll(tasks);

        // Wait for processing
        await Task.Delay(5000);

        // Count successful executions
        var successCount = 0;
        foreach (var jobId in successIds)
        {
            var job = await storage.GetJobByIdAsync(jobId);
            if (job?.Status == JobStatus.Succeeded)
                successCount++;
        }

        var retrySuccessCount = 0;
        foreach (var jobId in retryIds)
        {
            var job = await storage.GetJobByIdAsync(jobId);
            if (job?.Status == JobStatus.Succeeded && job.Attempts >= 2)
                retrySuccessCount++;
        }

        successCount.Should().Be(10, "all simple success jobs should succeed");
        retrySuccessCount.Should().Be(10, "all retry jobs should succeed after retry");

        await host.StopAsync();
    }

    [Fact]
    public async Task ParallelEnqueueWithStructuredInputs()
    {
        ResetTestState();

        using var host = BuildHost(s =>
        {
            s.AddTransient<DelayJob>();
        }, workers: 4);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();

        // Enqueue 50 jobs with structured input in parallel
        var jobIds = new List<JobId>(50);
        var lockObj = new object();

        var tasks = Enumerable.Range(0, 50)
            .Select(async _ =>
            {
                var input = new DelayJobInput(10); // Short delay
                var jobId = await scheduler.EnqueueAsync<DelayJob, DelayJobInput>(input);
                lock (lockObj)
                {
                    jobIds.Add(jobId);
                }
            });

        await Task.WhenAll(tasks);

        // Wait for execution
        await Task.Delay(3000);

        var succeededCount = 0;
        foreach (var jobId in jobIds)
        {
            var job = await storage.GetJobByIdAsync(jobId);
            if (job?.Status == JobStatus.Succeeded)
                succeededCount++;
        }

        succeededCount.Should().Be(50, "all delay jobs should execute with structured input");

        await host.StopAsync();
    }

    [Fact]
    public async Task HighConcurrencyWithFailureRecovery()
    {
        ResetTestState();

        using var host = BuildHost(s =>
        {
            s.AddTransient<FailOnceThenSucceedJob>();
        }, workers: 5);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();

        // Enqueue 40 jobs that will fail then succeed
        var jobIds = new List<JobId>(40);
        var lockObj = new object();

        var tasks = Enumerable.Range(0, 40)
            .Select(async _ =>
            {
                var jobId = await scheduler.EnqueueAsync<FailOnceThenSucceedJob>();
                lock (lockObj)
                {
                    jobIds.Add(jobId);
                }
            });

        await Task.WhenAll(tasks);

        // Wait for failure and retry recovery
        var deadline = DateTime.UtcNow.AddSeconds(15);
        var allRecovered = false;

        while (DateTime.UtcNow < deadline && !allRecovered)
        {
            var succeededCount = 0;
            foreach (var jobId in jobIds)
            {
                var job = await storage.GetJobByIdAsync(jobId);
                if (job?.Status == JobStatus.Succeeded)
                    succeededCount++;
            }

            if (succeededCount == 40)
            {
                allRecovered = true;
            }
            else
            {
                await Task.Delay(200);
            }
        }

        allRecovered.Should().BeTrue("all jobs should recover after retry");

        await host.StopAsync();
    }
}
