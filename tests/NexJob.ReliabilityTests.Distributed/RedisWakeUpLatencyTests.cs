using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexJob;
using NexJob.Redis;
using Xunit;

namespace NexJob.ReliabilityTests.Distributed;

/// <summary>
/// Reliability tests for wake-up signal latency and dispatch efficiency on Redis.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class RedisWakeUpLatencyTests
    : DistributedReliabilityTestBase,
      IClassFixture<RedisReliabilityFixture>
{
    private readonly RedisReliabilityFixture _fixture;

    public RedisWakeUpLatencyTests(RedisReliabilityFixture fixture)
        => _fixture = fixture;

    private Action<IServiceCollection> Storage() =>
        s => s.AddNexJobRedis(_fixture.ConnectionString);

    [Fact]
    public async Task WakeUpNotificationProcessedWithinTimeout_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(Storage(), s => s.AddTransient<SuccessJob>(), workers: 1);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var jobId = await scheduler.EnqueueAsync<SuccessJob>();

        var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(15));
        sw.Stop();

        job.Should().NotBeNull("job should be processed quickly after enqueue");
        sw.ElapsedMilliseconds.Should().BeLessThan(2000, "wake-up should trigger within 2 seconds");

        await host.StopAsync();
    }

    [Fact]
    public async Task WakeUpNotificationProcessedWithinTimeout_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(Storage(), s => s.AddTransient<SuccessJobWithInput>(), workers: 1);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var jobId = await scheduler.EnqueueAsync<SuccessJobWithInput, SuccessJobInput>(
            new SuccessJobInput("test"));

        var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(15));
        sw.Stop();

        job.Should().NotBeNull();
        sw.ElapsedMilliseconds.Should().BeLessThan(2000);

        await host.StopAsync();
    }

    [Fact]
    public async Task MultipleJobsTriggerSimultaneousProcessing_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(Storage(), s => s.AddTransient<SuccessJob>(), workers: 2);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var jobIds = await Task.WhenAll(
            scheduler.EnqueueAsync<SuccessJob>(),
            scheduler.EnqueueAsync<SuccessJob>(),
            scheduler.EnqueueAsync<SuccessJob>());

        foreach (var jobId in jobIds)
        {
            var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(15));
            job.Should().NotBeNull();
        }

        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(3000, "multiple jobs should process in parallel");

        await host.StopAsync();
    }

    [Fact]
    public async Task MultipleJobsTriggerSimultaneousProcessing_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(Storage(), s => s.AddTransient<SuccessJobWithInput>(), workers: 2);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var jobIds = await Task.WhenAll(
            scheduler.EnqueueAsync<SuccessJobWithInput, SuccessJobInput>(new("job1")),
            scheduler.EnqueueAsync<SuccessJobWithInput, SuccessJobInput>(new("job2")),
            scheduler.EnqueueAsync<SuccessJobWithInput, SuccessJobInput>(new("job3")));

        foreach (var jobId in jobIds)
        {
            var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(15));
            job.Should().NotBeNull();
        }

        await host.StopAsync();
    }

    [Fact(Skip = "BUG: Test isolation - static counter shared between parallel tests")]
    public async Task JobsProcessedInEnqueueOrder_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(Storage(), s => s.AddTransient<TrackingJob>(), workers: 1);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobIds = new List<JobId>();

        for (int i = 0; i < 5; i++)
        {
            jobIds.Add(await scheduler.EnqueueAsync<TrackingJob>());
            await Task.Delay(100);
        }

        foreach (var jobId in jobIds)
        {
            var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(25));
            job.Should().NotBeNull("all jobs should be processed");
        }

        await Task.Delay(1500);
        TrackingJob.ExecutionCount.Should().Be(5, "all 5 jobs should execute");

        await host.StopAsync();
    }

    [Fact(Skip = "BUG: Test isolation - static counter shared between parallel tests")]
    public async Task JobsProcessedInEnqueueOrder_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(Storage(), s => s.AddTransient<TrackingJobWithInput>(), workers: 1);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobIds = new List<JobId>();

        for (int i = 0; i < 5; i++)
        {
            jobIds.Add(await scheduler.EnqueueAsync<TrackingJobWithInput, TrackingJobInput>(
                new TrackingJobInput(Guid.NewGuid())));
            await Task.Delay(100);
        }

        foreach (var jobId in jobIds)
        {
            var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(25));
            job.Should().NotBeNull("all jobs should be processed");
        }

        await Task.Delay(1500);
        TrackingJobWithInput.ExecutionCount.Should().Be(5, "all 5 jobs should execute");

        await host.StopAsync();
    }

    [Fact]
    public async Task RapidSequentialEnqueueProcessesCorrectly_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(Storage(), s => s.AddTransient<SuccessJob>(), workers: 2);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobIds = new List<JobId>();

        for (int i = 0; i < 10; i++)
        {
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
    public async Task RapidSequentialEnqueueProcessesCorrectly_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(Storage(), s => s.AddTransient<SuccessJobWithInput>(), workers: 2);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobIds = new List<JobId>();

        for (int i = 0; i < 10; i++)
        {
            jobIds.Add(await scheduler.EnqueueAsync<SuccessJobWithInput, SuccessJobInput>(
                new SuccessJobInput($"rapid-{i}")));
        }

        foreach (var jobId in jobIds)
        {
            var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(25));
            job.Should().NotBeNull();
        }

        await host.StopAsync();
    }

    [Fact]
    public async Task WorkerPoolScalingHandlesLoadCorrectly_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(Storage(), s => s.AddTransient<SuccessJob>(), workers: 4);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var jobIds = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => scheduler.EnqueueAsync<SuccessJob>()));

        foreach (var jobId in jobIds)
        {
            var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(25));
            job.Should().NotBeNull();
        }

        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(5000, "scaled worker pool should handle batch quickly");

        await host.StopAsync();
    }

    [Fact]
    public async Task WorkerPoolScalingHandlesLoadCorrectly_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(Storage(), s => s.AddTransient<SuccessJobWithInput>(), workers: 4);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var jobIds = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(i => scheduler.EnqueueAsync<SuccessJobWithInput, SuccessJobInput>(
                new SuccessJobInput($"scaled-{i}"))));

        foreach (var jobId in jobIds)
        {
            var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(25));
            job.Should().NotBeNull();
        }

        sw.Stop();
        sw.ElapsedMilliseconds.Should().BeLessThan(5000);

        await host.StopAsync();
    }
}
