using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexJob;
using NexJob.MongoDB;
using Xunit;

namespace NexJob.ReliabilityTests.Distributed;

/// <summary>
/// Reliability tests for concurrency and duplicate prevention on Mongo.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class MongoConcurrencyTests
    : DistributedReliabilityTestBase,
      IClassFixture<MongoReliabilityFixture>
{
    private readonly MongoReliabilityFixture _fixture;

    public MongoConcurrencyTests(MongoReliabilityFixture fixture)
        => _fixture = fixture;

    private Action<IServiceCollection> Storage() =>
        s => s.AddNexJobMongoDB(_fixture.ConnectionString, databaseName: "nexjob_reliability");

    [Fact(Skip = "BUG: Known issue")]
    public async Task SingleJobNeverExecutesTwiceWithMultipleWorkers_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<TrackingJob>(),
            workers: 3);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<TrackingJob>();

        await Task.Delay(10000);

        TrackingJob.ExecutionCount.Should().Be(1, "job should execute exactly once despite multiple workers");

        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();
        var job = await storage.GetJobByIdAsync(jobId);
        job!.Status.Should().Be(JobStatus.Succeeded);

        await host.StopAsync();
    }

    [Fact(Skip = "BUG: Known issue")]
    public async Task SingleJobNeverExecutesTwiceWithMultipleWorkers_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<TrackingJobWithInput>(),
            workers: 3);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<TrackingJobWithInput, TrackingInput>(new TrackingInput(Guid.NewGuid()));

        await Task.Delay(10000);
        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();
        var job = await storage.GetJobByIdAsync(jobId);
        job!.Status.Should().Be(JobStatus.Succeeded);

        await host.StopAsync();
    }

    [Fact(Skip = "BUG: Known issue")]
    public async Task ConcurrentEnqueueOfMultipleJobsExecutesAll_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJob>(),
            workers: 2);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();

        _ = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => scheduler.EnqueueAsync<SuccessJob>()));

        await Task.Delay(10000);

        SuccessJob.ExecutionCount.Should().Be(5, "all 5 jobs should execute");

        await host.StopAsync();
    }

    [Fact(Skip = "BUG: Known issue")]
    public async Task ConcurrentEnqueueOfMultipleJobsExecutesAll_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJobWithInput>(),
            workers: 2);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();

        _ = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(i => scheduler.EnqueueAsync<SuccessJobWithInput, SuccessInput>(
                new SuccessInput($"concurrent-{i}"))));

        await Task.Delay(10000);
        await host.StopAsync();
    }

    [Fact(Skip = "BUG: High throughput test - resource contention in full suite")]
    public async Task HighThroughputJobsProcessCorrectly_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJob>(),
            workers: 2);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();

        for (int i = 0; i < 20; i++)
        {
            await scheduler.EnqueueAsync<SuccessJob>();
        }

        await Task.Delay(10000);

        SuccessJob.ExecutionCount.Should().Be(20);

        await host.StopAsync();
    }

    [Fact(Skip = "BUG: High throughput test - resource contention in full suite")]
    public async Task HighThroughputJobsProcessCorrectly_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJobWithInput>(),
            workers: 2);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();

        for (int i = 0; i < 20; i++)
        {
            await scheduler.EnqueueAsync<SuccessJobWithInput, SuccessInput>(
                new SuccessInput($"batch-{i}"));
        }

        await Task.Delay(10000);
        await host.StopAsync();
    }

    [Fact]
    public async Task BatchJobProcessingWithMultipleWorkers_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJob>(),
            workers: 3);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobIds = await Task.WhenAll(Enumerable.Range(0, 9)
            .Select(_ => scheduler.EnqueueAsync<SuccessJob>()));

        foreach (var jobId in jobIds)
        {
            var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(25));
            job.Should().NotBeNull();
        }

        await host.StopAsync();
    }

    [Fact]
    public async Task BatchJobProcessingWithMultipleWorkers_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJobWithInput>(),
            workers: 3);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobIds = await Task.WhenAll(Enumerable.Range(0, 9)
            .Select(i => scheduler.EnqueueAsync<SuccessJobWithInput, SuccessInput>(
                new SuccessInput($"batch-{i}"))));

        foreach (var jobId in jobIds)
        {
            var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(25));
            job.Should().NotBeNull();
        }

        await host.StopAsync();
    }

    [Fact]
    public async Task LargeQueueProcessingWithLoadBalancing_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJob>(),
            workers: 2);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobIds = new List<JobId>();

        for (int i = 0; i < 15; i++)
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
    public async Task LargeQueueProcessingWithLoadBalancing_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJobWithInput>(),
            workers: 2);

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobIds = new List<JobId>();

        for (int i = 0; i < 15; i++)
        {
            jobIds.Add(await scheduler.EnqueueAsync<SuccessJobWithInput, SuccessInput>(
                new SuccessInput($"queue-{i}")));
        }

        foreach (var jobId in jobIds)
        {
            var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, TimeSpan.FromSeconds(25));
            job.Should().NotBeNull();
        }

        await host.StopAsync();
    }
}
