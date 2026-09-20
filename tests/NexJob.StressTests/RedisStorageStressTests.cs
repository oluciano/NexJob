using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Redis;
using NexJob.Storage;
using NexJob.StressTests.Fixtures;
using StackExchange.Redis;
using Xunit;

namespace NexJob.StressTests;

[Trait("Category", "Stress")]
public sealed class RedisStorageStressTests : IClassFixture<RedisStressFixture>
{
    private readonly RedisStressFixture _fixture;

    public RedisStorageStressTests(RedisStressFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Redis_HighConcurrency_EnqueueAndProcess_CompletesWithoutConnectionDrops()
    {
        // Arrange
        const int totalJobs = 3000;
        const int concurrentWorkers = 20;
        StressJob.Reset();

        var redis = await ConnectionMultiplexer.ConnectAsync(_fixture.ConnectionString);
        var server = redis.GetServer(redis.GetEndPoints()[0]);
        await server.FlushDatabaseAsync();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJobRedis(_fixture.ConnectionString);
                services.AddNexJob(opt =>
                {
                    opt.Workers = concurrentWorkers;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(10);
                });

                services.AddTransient<StressJob>();
            })
            .Build();

        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        var sw = Stopwatch.StartNew();

        // Act: Enqueue totalJobs concurrently across 20 parallel threads
        Parallel.For(
            0,
            totalJobs,
            new ParallelOptions { MaxDegreeOfParallelism = 20, },
            i =>
            {
                scheduler.EnqueueAsync<StressJob, StressJobInput>(
                    new StressJobInput { Index = i, Payload = $"payload-redis-{i}", })
                    .GetAwaiter()
                    .GetResult();
            });

        // Wait for all jobs to be processed
        var waitSw = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(60);
        while (StressJob.ExecutionCount < totalJobs && waitSw.Elapsed < timeout)
        {
            await Task.Delay(100);
        }

        await host.StopAsync();
        sw.Stop();

        // Assert
        StressJob.ExecutionCount.Should().Be(totalJobs, "all enqueued jobs must be processed to completion in Redis");

        var storage = host.Services.GetRequiredService<IDashboardStorage>();
        var metrics = await storage.GetMetricsAsync();
        metrics.Processing.Should().Be(0);
        metrics.Enqueued.Should().Be(0);
        metrics.Succeeded.Should().Be(totalJobs);

        var totalThroughput = totalJobs / sw.Elapsed.TotalSeconds;
        Assert.True(totalThroughput > 30, $"Expected Redis throughput > 30 jobs/sec, but was {totalThroughput:F1} jobs/sec");
    }
}
