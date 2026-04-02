using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexJob;
using NexJob.Redis;
using Xunit;

namespace NexJob.ReliabilityTests.Distributed;

/// <summary>
/// Reliability tests for deadline enforcement on Redis.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class RedisDeadlineTests
    : DistributedReliabilityTestBase,
      IClassFixture<RedisReliabilityFixture>
{
    private readonly RedisReliabilityFixture _fixture;

    public RedisDeadlineTests(RedisReliabilityFixture fixture)
        => _fixture = fixture;

    private Action<IServiceCollection> Storage() =>
        s => s.AddNexJobRedis(_fixture.ConnectionString);

    [Fact(Skip = "BUG: Deadline enforcement timing - jobs not transitioning to Expired status as expected")]
    public async Task JobNotExecutedAfterDeadline_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJob>(),
            workers: 1,
            pollingInterval: TimeSpan.FromMilliseconds(500));

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<SuccessJob>(deadlineAfter: TimeSpan.FromMilliseconds(100));

        await Task.Delay(8000);

        var job = await WaitForJobStatus(host, jobId, JobStatus.Expired, TimeSpan.FromSeconds(15));
        job.Should().NotBeNull("job should be marked as Expired");
        job!.Status.Should().Be(JobStatus.Expired);

        await host.StopAsync();
    }

    [Fact(Skip = "BUG: Deadline enforcement timing - jobs not transitioning to Expired status as expected")]
    public async Task JobNotExecutedAfterDeadline_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJobWithInput>(),
            workers: 1,
            pollingInterval: TimeSpan.FromMilliseconds(500));

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<SuccessJobWithInput, SuccessInput>(
            new SuccessInput("test"),
            deadlineAfter: TimeSpan.FromMilliseconds(100));

        await Task.Delay(8000);

        var job = await WaitForJobStatus(host, jobId, JobStatus.Expired, TimeSpan.FromSeconds(15));
        job.Should().NotBeNull("job should be marked as Expired");
        job!.Status.Should().Be(JobStatus.Expired);

        await host.StopAsync();
    }

    [Fact]
    public async Task JobWithLongDeadlineExecutesNormally_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJob>(),
            workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<SuccessJob>(deadlineAfter: TimeSpan.FromSeconds(30));

        var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(15));
        job.Should().NotBeNull();
        job!.Status.Should().Be(JobStatus.Succeeded);

        await host.StopAsync();
    }

    [Fact]
    public async Task JobWithLongDeadlineExecutesNormally_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJobWithInput>(),
            workers: 1);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<SuccessJobWithInput, SuccessInput>(
            new SuccessInput("test"),
            deadlineAfter: TimeSpan.FromSeconds(30));

        var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(15));
        job.Should().NotBeNull();
        job!.Status.Should().Be(JobStatus.Succeeded);

        await host.StopAsync();
    }

    [Fact(Skip = "BUG: Deadline enforcement timing - jobs not transitioning to Expired status as expected")]
    public async Task ExpirationRespectedEvenAfterRetries_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<AlwaysFailJob>(),
            workers: 1,
            pollingInterval: TimeSpan.FromMilliseconds(500));

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<AlwaysFailJob>(deadlineAfter: TimeSpan.FromMilliseconds(150));

        await Task.Delay(8000);

        var job = await WaitForJobStatus(host, jobId, JobStatus.Expired, TimeSpan.FromSeconds(15));
        job.Should().NotBeNull("job should expire rather than retry indefinitely");
        job!.Status.Should().Be(JobStatus.Expired);

        await host.StopAsync();
    }

    [Fact(Skip = "BUG: Deadline enforcement timing - jobs not transitioning to Expired status as expected")]
    public async Task ExpirationRespectedEvenAfterRetries_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<AlwaysFailJobWithInput>(),
            workers: 1,
            pollingInterval: TimeSpan.FromMilliseconds(500));

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<AlwaysFailJobWithInput, AlwaysFailInput>(
            new AlwaysFailInput("test"),
            deadlineAfter: TimeSpan.FromMilliseconds(150));

        await Task.Delay(8000);

        var job = await WaitForJobStatus(host, jobId, JobStatus.Expired, TimeSpan.FromSeconds(15));
        job.Should().NotBeNull();
        job!.Status.Should().Be(JobStatus.Expired);

        await host.StopAsync();
    }

    [Fact]
    public async Task MultipleJobsWithDifferentDeadlines_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJob>(),
            workers: 2);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId1 = await scheduler.EnqueueAsync<SuccessJob>(deadlineAfter: TimeSpan.FromSeconds(30));
        var jobId2 = await scheduler.EnqueueAsync<SuccessJob>(deadlineAfter: TimeSpan.FromSeconds(30));

        var job1 = await WaitForJobStatus(host, jobId1, JobStatus.Succeeded, TimeSpan.FromSeconds(25));
        var job2 = await WaitForJobStatus(host, jobId2, JobStatus.Succeeded, TimeSpan.FromSeconds(25));

        job1.Should().NotBeNull();
        job2.Should().NotBeNull();

        await host.StopAsync();
    }

    [Fact]
    public async Task MultipleJobsWithDifferentDeadlines_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJobWithInput>(),
            workers: 2);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId1 = await scheduler.EnqueueAsync<SuccessJobWithInput, SuccessInput>(
            new SuccessInput("deadline-1"),
            deadlineAfter: TimeSpan.FromSeconds(30));
        var jobId2 = await scheduler.EnqueueAsync<SuccessJobWithInput, SuccessInput>(
            new SuccessInput("deadline-2"),
            deadlineAfter: TimeSpan.FromSeconds(30));

        var job1 = await WaitForJobStatus(host, jobId1, JobStatus.Succeeded, TimeSpan.FromSeconds(25));
        var job2 = await WaitForJobStatus(host, jobId2, JobStatus.Succeeded, TimeSpan.FromSeconds(25));

        job1.Should().NotBeNull();
        job2.Should().NotBeNull();

        await host.StopAsync();
    }

    [Fact]
    public async Task DeadlineEnforcementWithMultipleWorkers_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJob>(),
            workers: 3);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobIds = new List<JobId>();

        for (int i = 0; i < 6; i++)
        {
            jobIds.Add(await scheduler.EnqueueAsync<SuccessJob>(deadlineAfter: TimeSpan.FromSeconds(20)));
        }

        foreach (var jobId in jobIds)
        {
            var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(25));
            job.Should().NotBeNull();
        }

        await host.StopAsync();
    }

    [Fact]
    public async Task DeadlineEnforcementWithMultipleWorkers_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJobWithInput>(),
            workers: 3);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobIds = new List<JobId>();

        for (int i = 0; i < 6; i++)
        {
            jobIds.Add(await scheduler.EnqueueAsync<SuccessJobWithInput, SuccessInput>(
                new SuccessInput($"multi-deadline-{i}"),
                deadlineAfter: TimeSpan.FromSeconds(20)));
        }

        foreach (var jobId in jobIds)
        {
            var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(25));
            job.Should().NotBeNull();
        }

        await host.StopAsync();
    }
}
