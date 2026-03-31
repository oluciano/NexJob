using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexJob;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Reliability tests for crash recovery and system resilience.
/// Tests that jobs are not lost or duplicated when dispatcher crashes,
/// and validates state consistency across restarts.
///
/// Trait: Reliability
/// </summary>
[Trait("Category", "Reliability")]
public sealed class RecoveryTests : ReliabilityTestBase
{
    [Fact]
    public async Task JobNotLostOnDispatcherCrash()
    {
        ResetTestState();

        using var host = BuildHost(s =>
        {
            s.AddTransient<SuccessJob>();
        }, workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();

        var jobId = await scheduler.EnqueueAsync<SuccessJob>();

        // Stop dispatcher immediately (simulating crash)
        await host.StopAsync();

        // Job should still exist in storage
        var job = await storage.GetJobByIdAsync(jobId);
        job.Should().NotBeNull("job must not be lost on dispatcher crash");
    }

    [Fact]
    public async Task JobResumesAfterDispatcherRestart()
    {
        ResetTestState();

        JobId jobId;

        // First host: enqueue job and let it start
        using (var host1 = BuildHost(s =>
        {
            s.AddTransient<DelayJob>();
        }, workers: 1, pollingInterval: TimeSpan.FromSeconds(5))) // High polling to simulate stuck dispatcher
        {
            await host1.StartAsync();

            var scheduler = host1.Services.GetRequiredService<IScheduler>();
            jobId = await scheduler.EnqueueAsync<DelayJob, DelayJobInput>(new DelayJobInput(2000));

            // Wait a moment then stop (job may be in progress)
            await Task.Delay(500);
            await host1.StopAsync();
        }

        // Second host: restart and verify job completes
        using (var host2 = BuildHost(s =>
        {
            s.AddTransient<DelayJob>();
        }, workers: 1))
        {
            await host2.StartAsync();

            // Wait for job to complete
            var job = await WaitForJobStatus(host2, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(10));

            job.Should().NotBeNull("job should complete after dispatcher restart");
            job!.Status.Should().Be(JobStatus.Succeeded);

            await host2.StopAsync();
        }
    }

    [Fact]
    public async Task JobNotDuplicatedOnProcessingStateRecovery()
    {
        ResetTestState();

        using var host = BuildHost(s =>
        {
            s.AddTransient<TrackingJob>();
        }, workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var jobId = await scheduler.EnqueueAsync<TrackingJob>();

        // Wait for job to complete
        await Task.Delay(1000);

        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();
        var job = await storage.GetJobByIdAsync(jobId);
        job!.Status.Should().Be(JobStatus.Succeeded);

        // Even if job somehow remained in Processing state, it should not re-execute
        // This is validated by checking execution count
        TrackingJob.ExecutionCount.Should().Be(1, "job should execute exactly once");

        await host.StopAsync();
    }

    [Fact]
    public async Task FailedJobWithPendingRetryResumesProperly()
    {
        ResetTestState();

        JobId jobId;

        // First host: enqueue failing job
        using (var host1 = BuildHost(s =>
        {
            s.AddTransient<FailOnceThenSucceedJob>();
        }, workers: 1, pollingInterval: TimeSpan.FromSeconds(30))) // Very high polling to simulate stuck
        {
            await host1.StartAsync();

            var scheduler = host1.Services.GetRequiredService<IScheduler>();
            jobId = await scheduler.EnqueueAsync<FailOnceThenSucceedJob>();

            // Let first attempt fail
            await Task.Delay(500);
            await host1.StopAsync();
        }

        // Second host: restart and let retry execute
        using (var host2 = BuildHost(s =>
        {
            s.AddTransient<FailOnceThenSucceedJob>();
        }, workers: 1))
        {
            await host2.StartAsync();

            // Retry should execute and succeed
            var job = await WaitForJobStatus(host2, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(10));

            job.Should().NotBeNull("job should succeed on retry after restart");
            job!.Attempts.Should().Be(2, "job should have attempted twice");

            await host2.StopAsync();
        }
    }

    [Fact]
    public async Task StorageStateRemainsConsistentAcrossRestarts()
    {
        ResetTestState();

        var jobIds = new List<JobId>();

        // First host: enqueue multiple jobs
        using (var host1 = BuildHost(s =>
        {
            s.AddTransient<SuccessJob>();
        }, workers: 1))
        {
            await host1.StartAsync();

            var scheduler = host1.Services.GetRequiredService<IScheduler>();
            for (int i = 0; i < 5; i++)
            {
                jobIds.Add(await scheduler.EnqueueAsync<SuccessJob>());
            }

            // Let some execute
            await Task.Delay(1000);
            await host1.StopAsync();
        }

        // Second host: verify state is consistent
        using (var host2 = BuildHost(s =>
        {
            s.AddTransient<SuccessJob>();
        }, workers: 1))
        {
            await host2.StartAsync();

            var storage = host2.Services.GetRequiredService<Storage.IStorageProvider>();

            // All jobs should either be Succeeded or still Enqueued (never lost)
            foreach (var jobId in jobIds)
            {
                var job = await storage.GetJobByIdAsync(jobId);
                job.Should().NotBeNull();
                job!.Status.Should().BeOneOf(JobStatus.Succeeded, JobStatus.Enqueued, JobStatus.Processing);
            }

            // Wait for all to complete
            await Task.Delay(3000);

            // All should now be Succeeded
            var succeededCount = 0;
            foreach (var jobId in jobIds)
            {
                var job = await storage.GetJobByIdAsync(jobId);
                if (job?.Status == JobStatus.Succeeded)
                {
                    succeededCount++;
                }
            }

            succeededCount.Should().Be(5, "all jobs should eventually succeed");

            await host2.StopAsync();
        }
    }
}
