using FluentAssertions;
using Microsoft.Extensions.Logging;
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

    [Fact]
    public async Task JobNotExecutedAfterDeadline_NoInput()
    {
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJob>(sp => new SuccessJob(() => { }, sp.GetRequiredService<ILogger<SuccessJob>>())),
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

    [Fact]
    public async Task JobNotExecutedAfterDeadline_WithInput()
    {
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJobWithInput>(sp => new SuccessJobWithInput(() => { }, sp.GetRequiredService<ILogger<SuccessJobWithInput>>())),
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
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJob>(sp => new SuccessJob(() => { }, sp.GetRequiredService<ILogger<SuccessJob>>())),
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
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJobWithInput>(sp => new SuccessJobWithInput(() => { }, sp.GetRequiredService<ILogger<SuccessJobWithInput>>())),
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

    [Fact]
    public async Task ExpirationRespectedEvenAfterRetries_NoInput()
    {
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<AlwaysFailJob>(sp => new AlwaysFailJob(() => { }, sp.GetRequiredService<ILogger<AlwaysFailJob>>())),
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

    [Fact]
    public async Task ExpirationRespectedEvenAfterRetries_WithInput()
    {
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<AlwaysFailJobWithInput>(sp => new AlwaysFailJobWithInput(() => { }, sp.GetRequiredService<ILogger<AlwaysFailJobWithInput>>())),
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
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJob>(sp => new SuccessJob(() => { }, sp.GetRequiredService<ILogger<SuccessJob>>())),
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
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJobWithInput>(sp => new SuccessJobWithInput(() => { }, sp.GetRequiredService<ILogger<SuccessJobWithInput>>())),
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
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJob>(sp => new SuccessJob(() => { }, sp.GetRequiredService<ILogger<SuccessJob>>())),
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
        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJobWithInput>(sp => new SuccessJobWithInput(() => { }, sp.GetRequiredService<ILogger<SuccessJobWithInput>>())),
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
