using System;
using System.Diagnostics;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Postgres;
using NexJob.Storage;
using NexJob.StressTests.Fixtures;
using Npgsql;
using Xunit;

namespace NexJob.StressTests;

[Trait("Category", "Stress")]
public sealed class PostgresStorageStressTests : IClassFixture<PostgresStressFixture>
{
    private readonly PostgresStressFixture _fixture;

    public PostgresStorageStressTests(PostgresStressFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Postgres_HighConcurrency_EnqueueAndProcess_CompletesWithoutDeadlocksOrStarvation()
    {
        // Arrange
        const int totalJobs = 3000;
        const int concurrentWorkers = 20;
        StressJob.Reset();

        var baseConn = _fixture.ConnectionString;
        var dbName = $"nexjob_stress_{Guid.NewGuid():N}";

        await using (var adminConn = new NpgsqlConnection(baseConn))
        {
            await adminConn.OpenAsync();
            await using var cmd = adminConn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE {dbName};";
            await cmd.ExecuteNonQueryAsync();
        }

        var connBuilder = new NpgsqlConnectionStringBuilder(baseConn)
        {
            Database = dbName,
            MaxPoolSize = 50,
            MinPoolSize = 5,
        };

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJobPostgres(connBuilder.ConnectionString);
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
                    new StressJobInput { Index = i, Payload = $"payload-{i}", })
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
        StressJob.ExecutionCount.Should().Be(totalJobs, "all enqueued jobs must be processed to completion without deadlocks");

        // Verify storage state via IDashboardStorage
        var storage = host.Services.GetRequiredService<IDashboardStorage>();
        var metrics = await storage.GetMetricsAsync();
        metrics.Processing.Should().Be(0);
        metrics.Enqueued.Should().Be(0);
        metrics.Succeeded.Should().Be(totalJobs);
        metrics.Failed.Should().Be(0);

        // Throughput assertion
        var totalThroughput = totalJobs / sw.Elapsed.TotalSeconds;
        Assert.True(totalThroughput > 30, $"Expected throughput > 30 jobs/sec, but was {totalThroughput:F1} jobs/sec");
    }
}
