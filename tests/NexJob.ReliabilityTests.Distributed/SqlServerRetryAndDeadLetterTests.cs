using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexJob;
using NexJob.SqlServer;
using Xunit;

namespace NexJob.ReliabilityTests.Distributed;

/// <summary>
/// Reliability tests for retry behavior and dead-letter handler invocation on SQL Server.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class SqlServerRetryAndDeadLetterTests
    : DistributedReliabilityTestBase,
      IClassFixture<SqlServerReliabilityFixture>
{
    private readonly SqlServerReliabilityFixture _fixture;

    public SqlServerRetryAndDeadLetterTests(SqlServerReliabilityFixture fixture)
        => _fixture = fixture;

    private Action<IServiceCollection> Storage() =>
        s => s.AddNexJobSqlServer(_fixture.ConnectionString);

    [Fact]
    public async Task RetryExecutesCorrectlyAfterFailure_NoInput()
    {
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<FailOnceThenSucceedJob>(sp => new FailOnceThenSucceedJob(() => { }, sp.GetRequiredService<ILogger<FailOnceThenSucceedJob>>())),
            workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<FailOnceThenSucceedJob>();

        var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(20));

        job.Should().NotBeNull();
        job!.Attempts.Should().Be(2);

        await host.StopAsync();
    }

    [Fact]
    public async Task RetryExecutesCorrectlyAfterFailure_WithInput()
    {
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<FailOnceThenSucceedJobWithInput>(sp => new FailOnceThenSucceedJobWithInput(() => { }, sp.GetRequiredService<ILogger<FailOnceThenSucceedJobWithInput>>())),
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

    [Fact]
    public async Task DeadLetterHandlerInvokedAfterMaxAttemptsExhausted_NoInput()
    {
        RecordingDeadLetterHandler<AlwaysFailJob>.Reset();

        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<AlwaysFailJob>(sp => new AlwaysFailJob(() => { }, sp.GetRequiredService<ILogger<AlwaysFailJob>>()));
                s.AddTransient<IDeadLetterHandler<AlwaysFailJob>, RecordingDeadLetterHandler<AlwaysFailJob>>();
            },
            workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<AlwaysFailJob>();

        await Task.Delay(10000);

        RecordingDeadLetterHandler<AlwaysFailJob>.InvocationCount.Should().Be(1);
        RecordingDeadLetterHandler<AlwaysFailJob>.LastFailedJob.Should().NotBeNull();
        RecordingDeadLetterHandler<AlwaysFailJob>.LastFailedJob!.Id.Should().Be(jobId);

        var job = await WaitForJobStatus(host, jobId, JobStatus.Failed, TimeSpan.FromSeconds(15));
        job.Should().NotBeNull();

        await host.StopAsync();
    }

    [Fact]
    public async Task DeadLetterHandlerInvokedAfterMaxAttemptsExhausted_WithInput()
    {
        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<AlwaysFailJobWithInput>(sp => new AlwaysFailJobWithInput(() => { }, sp.GetRequiredService<ILogger<AlwaysFailJobWithInput>>()));
                s.AddTransient<IDeadLetterHandler<AlwaysFailJobWithInput>, RecordingDeadLetterHandler<AlwaysFailJobWithInput>>();
            },
            workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<AlwaysFailJobWithInput, AlwaysFailInput>(
            new AlwaysFailInput("test"));

        await Task.Delay(10000);

        RecordingDeadLetterHandler<AlwaysFailJobWithInput>.InvocationCount.Should().Be(1);
        RecordingDeadLetterHandler<AlwaysFailJobWithInput>.LastFailedJob.Should().NotBeNull();
        RecordingDeadLetterHandler<AlwaysFailJobWithInput>.LastFailedJob!.Id.Should().Be(jobId);

        var job = await WaitForJobStatus(host, jobId, JobStatus.Failed, TimeSpan.FromSeconds(15));
        job.Should().NotBeNull();

        await host.StopAsync();
    }

    [Fact]
    public async Task DeadLetterHandlerExceptionDoesNotCrashDispatcher_NoInput()
    {
        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<AlwaysFailJob>(sp => new AlwaysFailJob(() => { }, sp.GetRequiredService<ILogger<AlwaysFailJob>>()));
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

    [Fact]
    public async Task DeadLetterHandlerExceptionDoesNotCrashDispatcher_WithInput()
    {
        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<AlwaysFailJobWithInput>(sp => new AlwaysFailJobWithInput(() => { }, sp.GetRequiredService<ILogger<AlwaysFailJobWithInput>>()));
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

    [Fact]
    public async Task MultipleJobsWithDifferentRetryBehavior_NoInput()
    {
        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<SuccessJob>(sp => new SuccessJob(() => { }, sp.GetRequiredService<ILogger<SuccessJob>>()));
                s.AddTransient<FailOnceThenSucceedJob>(sp => new FailOnceThenSucceedJob(() => { }, sp.GetRequiredService<ILogger<FailOnceThenSucceedJob>>()));
            },
            workers: 2);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId1 = await scheduler.EnqueueAsync<SuccessJob>();
        var jobId2 = await scheduler.EnqueueAsync<FailOnceThenSucceedJob>();

        var job1 = await WaitForJobStatus(host, jobId1, JobStatus.Succeeded, TimeSpan.FromSeconds(25));
        var job2 = await WaitForJobStatus(host, jobId2, JobStatus.Succeeded, TimeSpan.FromSeconds(25));

        job1.Should().NotBeNull();
        job2.Should().NotBeNull();

        await host.StopAsync();
    }

    [Fact]
    public async Task MultipleJobsWithDifferentRetryBehavior_WithInput()
    {
        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<SuccessJobWithInput>(sp => new SuccessJobWithInput(() => { }, sp.GetRequiredService<ILogger<SuccessJobWithInput>>()));
                s.AddTransient<FailOnceThenSucceedJobWithInput>(sp => new FailOnceThenSucceedJobWithInput(() => { }, sp.GetRequiredService<ILogger<FailOnceThenSucceedJobWithInput>>()));
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

    [Fact]
    public async Task JobSequenceWithRetryAndSuccess_NoInput()
    {
        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<FailOnceThenSucceedJob>(sp => new FailOnceThenSucceedJob(() => { }, sp.GetRequiredService<ILogger<FailOnceThenSucceedJob>>()));
                s.AddTransient<SuccessJob>(sp => new SuccessJob(() => { }, sp.GetRequiredService<ILogger<SuccessJob>>()));
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

    [Fact]
    public async Task JobSequenceWithRetryAndSuccess_WithInput()
    {
        using var host = BuildHost(
            Storage(),
            s =>
            {
                s.AddTransient<FailOnceThenSucceedJobWithInput>(sp => new FailOnceThenSucceedJobWithInput(() => { }, sp.GetRequiredService<ILogger<FailOnceThenSucceedJobWithInput>>()));
                s.AddTransient<SuccessJobWithInput>(sp => new SuccessJobWithInput(() => { }, sp.GetRequiredService<ILogger<SuccessJobWithInput>>()));
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
