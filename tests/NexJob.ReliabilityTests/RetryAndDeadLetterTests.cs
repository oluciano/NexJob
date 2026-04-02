using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexJob;
using Xunit;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Reliability tests for retry behavior and dead-letter handler invocation.
/// Tests real retry execution, handler invocation on permanent failure,
/// and handler exception resilience.
///
/// Trait: Reliability
/// </summary>
[Trait("Category", "Reliability")]
public sealed class RetryAndDeadLetterTests : ReliabilityTestBase
{
    [Fact]
    public async Task RetryExecutesCorrectlyAfterFailure()
    {
        ResetTestState();

        using var host = BuildHost(s =>
        {
            s.AddTransient<FailOnceThenSucceedJob>();
        }, workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<FailOnceThenSucceedJob>();

        // Wait for job to succeed (should retry after first failure)
        var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(10));

        job.Should().NotBeNull("job should eventually succeed after retry");
        job!.Attempts.Should().Be(2, "job should have attempted twice");
        FailOnceThenSucceedJob.ExecutionCount.Should().Be(2, "job should execute twice (fail then succeed)");

        await host.StopAsync();
    }

    [Fact]
    public async Task DeadLetterHandlerInvokedAfterMaxAttemptsExhausted()
    {
        ResetTestState();
        RecordingDeadLetterHandler<AlwaysFailJob>.Reset();

        using var host = BuildHost(s =>
        {
            s.AddTransient<AlwaysFailJob>();
            s.AddTransient<IDeadLetterHandler<AlwaysFailJob>, RecordingDeadLetterHandler<AlwaysFailJob>>();
        }, workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<AlwaysFailJob>();

        // Wait for handler to be invoked
        var maxWait = 0;
        while (RecordingDeadLetterHandler<AlwaysFailJob>.InvocationCount == 0 && maxWait < 50)
        {
            await Task.Delay(100);
            maxWait++;
        }

        RecordingDeadLetterHandler<AlwaysFailJob>.InvocationCount.Should().Be(1, "handler should be invoked exactly once");
        RecordingDeadLetterHandler<AlwaysFailJob>.LastFailedJob.Should().NotBeNull();
        RecordingDeadLetterHandler<AlwaysFailJob>.LastFailedJob!.Id.Should().Be(jobId);
        RecordingDeadLetterHandler<AlwaysFailJob>.LastException.Should().NotBeNull();

        // Verify job is marked as dead-letter (no longer scheduled for retry)
        var job = await WaitForJobStatus(host, jobId, JobStatus.Failed, TimeSpan.FromSeconds(5));
        job.Should().NotBeNull("job should be in Failed terminal state");
        job!.RetryAt.Should().BeNull("job should not be scheduled for retry");

        await host.StopAsync();
    }

    [Fact]
    public async Task DeadLetterHandlerReceivesCorrectJobContext()
    {
        ResetTestState();
        RecordingDeadLetterHandler<AlwaysFailJob>.Reset();

        using var host = BuildHost(s =>
        {
            s.AddTransient<AlwaysFailJob>();
            s.AddTransient<IDeadLetterHandler<AlwaysFailJob>, RecordingDeadLetterHandler<AlwaysFailJob>>();
        }, workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<AlwaysFailJob>();

        // Wait for handler
        await Task.Delay(3000);

        var recordedJob = RecordingDeadLetterHandler<AlwaysFailJob>.LastFailedJob;
        recordedJob.Should().NotBeNull();
        recordedJob!.Id.Should().Be(jobId);
        recordedJob.Status.Should().Be(JobStatus.Failed);
        recordedJob.Attempts.Should().BeGreaterThanOrEqualTo(1);
        recordedJob.JobType.Should().Contain("AlwaysFailJob");

        var exception = RecordingDeadLetterHandler<AlwaysFailJob>.LastException;
        exception.Should().NotBeNull();
        exception!.Message.Should().Contain("intentionally failed");

        await host.StopAsync();
    }

    [Fact]
    public async Task DeadLetterHandlerExceptionDoesNotCrashDispatcher()
    {
        ResetTestState();

        using var host = BuildHost(s =>
        {
            s.AddTransient<AlwaysFailJob>();
            s.AddTransient<IDeadLetterHandler<AlwaysFailJob>, ThrowingDeadLetterHandler<AlwaysFailJob>>();
        }, workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<AlwaysFailJob>();

        // Wait for job to fail and handler to be invoked (and throw)
        await Task.Delay(3000);

        // Job should still be in storage despite handler throwing
        var job = await WaitForJobStatus(host, jobId, JobStatus.Failed, TimeSpan.FromSeconds(2));
        job.Should().NotBeNull("job should be persisted despite handler exception");

        // Should be able to enqueue more jobs
        var jobId2 = await scheduler.EnqueueAsync<SuccessJob>();
        var successJob = await WaitForJobStatus(host, jobId2, JobStatus.Succeeded, TimeSpan.FromSeconds(5));
        successJob.Should().NotBeNull("dispatcher should continue processing after handler exception");

        await host.StopAsync();
    }

    [Fact]
    public async Task MultipleFailuresProgressThroughRetries()
    {
        ResetTestState();

        using var host = BuildHost(s =>
        {
            s.AddTransient<AlwaysFailJob>();
        }, workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<AlwaysFailJob>();

        // Allow time for multiple retry attempts
        await Task.Delay(2000);

        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();
        var job = await storage.GetJobByIdAsync(jobId);

        // Should have attempted multiple times
        job.Should().NotBeNull();
        job!.Attempts.Should().BeGreaterThanOrEqualTo(2, "job should attempt multiple times");

        await host.StopAsync();
    }

    [Fact]
    public async Task DispatcherContinuesProcessingAfterDeadLetterHandlerThrows_Deterministic()
    {
        ResetTestState();

        using var host = BuildHost(s =>
        {
            s.AddTransient<AlwaysFailJob>();
            s.AddTransient<IDeadLetterHandler<AlwaysFailJob>, ThrowingDeadLetterHandler<AlwaysFailJob>>();
            s.AddTransient<SuccessJob>();
        }, workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();

        // Step 1: Enqueue job that will fail and trigger handler that throws
        var failingJobId = await scheduler.EnqueueAsync<AlwaysFailJob>();

        // Step 2: Wait deterministically for the failing job to reach Failed state
        // This ensures all 3 retries are exhausted AND the dead-letter handler is invoked
        var failedJob = await WaitForJobStatus(
            host, failingJobId, JobStatus.Failed, TimeSpan.FromSeconds(10));

        failedJob.Should().NotBeNull(
            "job should be persisted in Failed state despite handler throwing");

        // Step 3: Immediately enqueue a job that succeeds
        // If dispatcher hung in the dead-letter handler, this job will never start
        var successJobId = await scheduler.EnqueueAsync<SuccessJob>();

        // Step 4: Wait deterministically for the success job to complete
        // If dispatcher hung after the handler threw, this will timeout and fail
        var successJob = await WaitForJobStatus(
            host, successJobId, JobStatus.Succeeded, TimeSpan.FromSeconds(10));

        successJob.Should().NotBeNull(
            "dispatcher should continue processing after dead-letter handler throws");
        successJob!.Status.Should().Be(JobStatus.Succeeded,
            "if we got here, the dispatcher is still alive and processing jobs");

        await host.StopAsync();
    }
}
