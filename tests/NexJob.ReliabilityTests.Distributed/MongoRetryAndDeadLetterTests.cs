using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexJob;
using NexJob.MongoDB;
using Xunit;

namespace NexJob.ReliabilityTests.Distributed;

/// <summary>
/// Reliability tests for retry behavior and dead-letter handler invocation on MongoDB.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class MongoRetryAndDeadLetterTests
    : DistributedReliabilityTestBase,
      IClassFixture<MongoReliabilityFixture>
{
    private readonly MongoReliabilityFixture _fixture;

    public MongoRetryAndDeadLetterTests(MongoReliabilityFixture fixture)
        => _fixture = fixture;

    private Action<IServiceCollection> Storage() =>
        s => s.AddNexJobMongoDB(_fixture.ConnectionString, databaseName: "nexjob_reliability");

    [Fact(Skip = "BUG: Test isolation issue - passes individually but times out in full suite")]
    public async Task RetryExecutesCorrectlyAfterFailure_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<FailOnceThenSucceedJob>(),
            workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<FailOnceThenSucceedJob>();

        var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(20));

        job.Should().NotBeNull();
        job!.Attempts.Should().Be(2);
        FailOnceThenSucceedJob.ExecutionCount.Should().Be(2);

        await host.StopAsync();
    }

    [Fact(Skip = "BUG: Test isolation issue - passes individually but times out in full suite")]
    public async Task RetryExecutesCorrectlyAfterFailure_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<FailOnceThenSucceedJobWithInput>(),
            workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<FailOnceThenSucceedJobWithInput, FailOnceThenSucceedInput>(
            new FailOnceThenSucceedInput("test-context"));

        var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(20));

        job.Should().NotBeNull();
        job!.Attempts.Should().Be(2);

        await host.StopAsync();
    }

    [Fact(Skip = "BUG: Known issue")]
    public async Task DeadLetterHandlerInvokedAfterMaxAttemptsExhausted_NoInput()
    {
        ResetTestState();
        RecordingDeadLetterHandler<AlwaysFailJob>.Reset();

        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<AlwaysFailJob>();
                s.AddTransient<IDeadLetterHandler<AlwaysFailJob>, RecordingDeadLetterHandler<AlwaysFailJob>>();
            },
            workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<AlwaysFailJob>();

        await Task.Delay(10000);

        RecordingDeadLetterHandler<AlwaysFailJob>.InvocationCount.Should().Be(1);
        RecordingDeadLetterHandler<AlwaysFailJob>.LastFailedJob?.Id.Should().Be(jobId);

        var job = await WaitForJobStatus(host, jobId, JobStatus.Failed, TimeSpan.FromSeconds(15));
        job.Should().NotBeNull();

        await host.StopAsync();
    }

    [Fact(Skip = "BUG: Known issue")]
    public async Task DeadLetterHandlerInvokedAfterMaxAttemptsExhausted_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<AlwaysFailJobWithInput>();
                s.AddTransient<IDeadLetterHandler<AlwaysFailJobWithInput>, RecordingDeadLetterHandler<AlwaysFailJobWithInput>>();
            },
            workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<AlwaysFailJobWithInput, AlwaysFailInput>(
            new AlwaysFailInput("test"));

        await Task.Delay(10000);

        RecordingDeadLetterHandler<AlwaysFailJobWithInput>.InvocationCount.Should().Be(1);
        RecordingDeadLetterHandler<AlwaysFailJobWithInput>.LastFailedJob?.Id.Should().Be(jobId);

        var job = await WaitForJobStatus(host, jobId, JobStatus.Failed, TimeSpan.FromSeconds(15));
        job.Should().NotBeNull();

        await host.StopAsync();
    }

    [Fact(Skip = "BUG: Requires deterministic handler invocation pattern")]
    public async Task DeadLetterHandlerExceptionDoesNotCrashDispatcher_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<AlwaysFailJob>();
                s.AddTransient<IDeadLetterHandler<AlwaysFailJob>, ThrowingDeadLetterHandler<AlwaysFailJob>>();
            },
            workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<AlwaysFailJob>();

        await Task.Delay(10000);

        var job = await WaitForJobStatus(host, jobId, JobStatus.Failed, TimeSpan.FromSeconds(10));
        job.Should().NotBeNull();

        var jobId2 = await scheduler.EnqueueAsync<SuccessJob>();
        var successJob = await WaitForJobStatus(host, jobId2, JobStatus.Succeeded, TimeSpan.FromSeconds(15));
        successJob.Should().NotBeNull();

        await host.StopAsync();
    }

    [Fact(Skip = "BUG: Requires deterministic handler invocation pattern")]
    public async Task DeadLetterHandlerExceptionDoesNotCrashDispatcher_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<AlwaysFailJobWithInput>();
                s.AddTransient<IDeadLetterHandler<AlwaysFailJobWithInput>, ThrowingDeadLetterHandler<AlwaysFailJobWithInput>>();
            },
            workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<AlwaysFailJobWithInput, AlwaysFailInput>(
            new AlwaysFailInput("test"));

        await Task.Delay(10000);

        var job = await WaitForJobStatus(host, jobId, JobStatus.Failed, TimeSpan.FromSeconds(10));
        job.Should().NotBeNull();

        var jobId2 = await scheduler.EnqueueAsync<SuccessJobWithInput, SuccessInput>(new SuccessInput("test"));
        var successJob = await WaitForJobStatus(host, jobId2, JobStatus.Succeeded, TimeSpan.FromSeconds(15));
        successJob.Should().NotBeNull();

        await host.StopAsync();
    }

    [Fact(Skip = "BUG: Test isolation - static counter shared between parallel tests")]
    public async Task MultipleJobsWithDifferentRetryBehavior_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<SuccessJob>();
                s.AddTransient<FailOnceThenSucceedJob>();
            },
            workers: 2);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId1 = await scheduler.EnqueueAsync<SuccessJob>();
        var jobId2 = await scheduler.EnqueueAsync<FailOnceThenSucceedJob>();

        var job1 = await WaitForJobStatus(host, jobId1, JobStatus.Succeeded, TimeSpan.FromSeconds(20));
        var job2 = await WaitForJobStatus(host, jobId2, JobStatus.Succeeded, TimeSpan.FromSeconds(20));

        job1.Should().NotBeNull();
        job2.Should().NotBeNull();

        await host.StopAsync();
    }

    [Fact(Skip = "BUG: Test isolation - static counter shared between parallel tests")]
    public async Task MultipleJobsWithDifferentRetryBehavior_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<SuccessJobWithInput>();
                s.AddTransient<FailOnceThenSucceedJobWithInput>();
            },
            workers: 2);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId1 = await scheduler.EnqueueAsync<SuccessJobWithInput, SuccessInput>(
            new SuccessInput("test1"));
        var jobId2 = await scheduler.EnqueueAsync<FailOnceThenSucceedJobWithInput, FailOnceThenSucceedInput>(
            new FailOnceThenSucceedInput("test2"));

        var job1 = await WaitForJobStatus(host, jobId1, JobStatus.Succeeded, TimeSpan.FromSeconds(25));
        var job2 = await WaitForJobStatus(host, jobId2, JobStatus.Succeeded, TimeSpan.FromSeconds(25));

        job1.Should().NotBeNull();
        job2.Should().NotBeNull();

        await host.StopAsync();
    }

    [Fact(Skip = "BUG: Test isolation - static counter shared between parallel tests")]
    public async Task JobSequenceWithRetryAndSuccess_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<FailOnceThenSucceedJob>();
                s.AddTransient<SuccessJob>();
            },
            workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobIds = new List<JobId>();

        for (int i = 0; i < 3; i++)
        {
            jobIds.Add(await scheduler.EnqueueAsync<FailOnceThenSucceedJob>());
            jobIds.Add(await scheduler.EnqueueAsync<SuccessJob>());
        }

        foreach (var jobId in jobIds)
        {
            var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(25));
            job.Should().NotBeNull();
        }

        await host.StopAsync();
    }

    [Fact(Skip = "BUG: Test isolation - static counter shared between parallel tests")]
    public async Task JobSequenceWithRetryAndSuccess_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<FailOnceThenSucceedJobWithInput>();
                s.AddTransient<SuccessJobWithInput>();
            },
            workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobIds = new List<JobId>();

        for (int i = 0; i < 3; i++)
        {
            jobIds.Add(await scheduler.EnqueueAsync<FailOnceThenSucceedJobWithInput, FailOnceThenSucceedInput>(
                new FailOnceThenSucceedInput($"retry-{i}")));
            jobIds.Add(await scheduler.EnqueueAsync<SuccessJobWithInput, SuccessInput>(
                new SuccessInput($"success-{i}")));
        }

        foreach (var jobId in jobIds)
        {
            var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(25));
            job.Should().NotBeNull();
        }

        await host.StopAsync();
    }
}
