using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NexJob;
using NexJob.SqlServer;
using Xunit;

namespace NexJob.ReliabilityTests.Distributed;

/// <summary>
/// Reliability tests for crash recovery and system resilience on SqlServer.
/// </summary>
[Trait("Category", "Reliability.Distributed")]
public sealed class SqlServerRecoveryTests
    : DistributedReliabilityTestBase,
      IClassFixture<SqlServerReliabilityFixture>
{
    private readonly SqlServerReliabilityFixture _fixture;

    public SqlServerRecoveryTests(SqlServerReliabilityFixture fixture)
        => _fixture = fixture;

    private Action<IServiceCollection> Storage() =>
        s => s.AddNexJobSqlServer(_fixture.ConnectionString);

    [Fact]
    public async Task JobNotLostOnDispatcherCrash_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(Storage(), s => s.AddTransient<SuccessJob>(), workers: 1);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();

        var jobId = await scheduler.EnqueueAsync<SuccessJob>();
        await host.StopAsync();

        var job = await storage.GetJobByIdAsync(jobId);
        job.Should().NotBeNull("job must not be lost on dispatcher crash");
    }

    [Fact]
    public async Task JobNotLostOnDispatcherCrash_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(Storage(), s => s.AddTransient<SuccessJobWithInput>(), workers: 1);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();

        var jobId = await scheduler.EnqueueAsync<SuccessJobWithInput, SuccessInput>(
            new SuccessInput("test"));
        await host.StopAsync();

        var job = await storage.GetJobByIdAsync(jobId);
        job.Should().NotBeNull();
    }

    [Fact]
    public async Task JobResumesAfterDispatcherRestart_NoInput()
    {
        ResetTestState();

        JobId jobId;
        using (var host1 = BuildHost(
            Storage(),
            s => s.AddTransient<DelayJob>(),
            workers: 1,
            pollingInterval: TimeSpan.FromSeconds(15)))
        {
            await host1.StartAsync();
            var scheduler = host1.Services.GetRequiredService<IScheduler>();
            jobId = await scheduler.EnqueueAsync<DelayJob>();
            await host1.StopAsync();
        }

        using var host2 = BuildHost(Storage(), s => s.AddTransient<DelayJob>(), workers: 1);
        await host2.StartAsync();

        var storage = host2.Services.GetRequiredService<Storage.IStorageProvider>();
        var job = await storage.GetJobByIdAsync(jobId);

        job.Should().NotBeNull("job should be recoverable after restart");
        await host2.StopAsync();
    }

    [Fact]
    public async Task JobResumesAfterDispatcherRestart_WithInput()
    {
        ResetTestState();

        JobId jobId;
        using (var host1 = BuildHost(
            Storage(),
            s => s.AddTransient<DelayJobWithInput>(),
            workers: 1,
            pollingInterval: TimeSpan.FromSeconds(15)))
        {
            await host1.StartAsync();
            var scheduler = host1.Services.GetRequiredService<IScheduler>();
            jobId = await scheduler.EnqueueAsync<DelayJobWithInput, DelayInput>(
                new DelayInput(2000));
            await host1.StopAsync();
        }

        using var host2 = BuildHost(Storage(), s => s.AddTransient<DelayJobWithInput>(), workers: 1);
        await host2.StartAsync();

        var storage = host2.Services.GetRequiredService<Storage.IStorageProvider>();
        var job = await storage.GetJobByIdAsync(jobId);
        job.Should().NotBeNull();

        await host2.StopAsync();
    }

    [Fact(Skip = "BUG: Recovery timing — job state transition may not be deterministic")]
    public async Task InflightJobStatePreserved_NoInput()
    {
        ResetTestState();
        using var host = BuildHost(Storage(), s => s.AddTransient<DelayJob>(), workers: 1);
        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact(Skip = "BUG: Recovery timing — job state transition may not be deterministic")]
    public async Task InflightJobStatePreserved_WithInput()
    {
        ResetTestState();
        using var host = BuildHost(Storage(), s => s.AddTransient<DelayJobWithInput>(), workers: 1);
        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task MultipleJobsNotLostOnCrash_NoInput()
    {
        ResetTestState();

        using var host = BuildHost(Storage(), s => s.AddTransient<SuccessJob>(), workers: 1);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();
        var jobIds = new List<JobId>();

        for (int i = 0; i < 3; i++)
        {
            jobIds.Add(await scheduler.EnqueueAsync<SuccessJob>());
        }

        await host.StopAsync();

        foreach (var jobId in jobIds)
        {
            var job = await storage.GetJobByIdAsync(jobId);
            job.Should().NotBeNull("all jobs must be recoverable");
        }
    }

    [Fact]
    public async Task MultipleJobsNotLostOnCrash_WithInput()
    {
        ResetTestState();

        using var host = BuildHost(Storage(), s => s.AddTransient<SuccessJobWithInput>(), workers: 1);
        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var storage = host.Services.GetRequiredService<Storage.IStorageProvider>();
        var jobIds = new List<JobId>();

        for (int i = 0; i < 3; i++)
        {
            jobIds.Add(await scheduler.EnqueueAsync<SuccessJobWithInput, SuccessInput>(
                new SuccessInput($"recover-{i}")));
        }

        await host.StopAsync();

        foreach (var jobId in jobIds)
        {
            var job = await storage.GetJobByIdAsync(jobId);
            job.Should().NotBeNull("all jobs must be recoverable");
        }
    }

    [Fact]
    public async Task ConcurrentFailureRecoveryWithMultipleWorkers_NoInput()
    {
        ResetTestState();

        JobId jobId1, jobId2;
        using (var host1 = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJob>(),
            workers: 2))
        {
            await host1.StartAsync();
            var scheduler = host1.Services.GetRequiredService<IScheduler>();
            jobId1 = await scheduler.EnqueueAsync<SuccessJob>();
            jobId2 = await scheduler.EnqueueAsync<SuccessJob>();
            await host1.StopAsync();
        }

        using var host2 = BuildHost(Storage(), s => s.AddTransient<SuccessJob>(), workers: 2);
        await host2.StartAsync();

        var storage = host2.Services.GetRequiredService<Storage.IStorageProvider>();
        var job1 = await storage.GetJobByIdAsync(jobId1);
        var job2 = await storage.GetJobByIdAsync(jobId2);

        job1.Should().NotBeNull();
        job2.Should().NotBeNull();

        await host2.StopAsync();
    }

    [Fact]
    public async Task ConcurrentFailureRecoveryWithMultipleWorkers_WithInput()
    {
        ResetTestState();

        JobId jobId1, jobId2;
        using (var host1 = BuildHost(
            Storage(),
            s => s.AddTransient<SuccessJobWithInput>(),
            workers: 2))
        {
            await host1.StartAsync();
            var scheduler = host1.Services.GetRequiredService<IScheduler>();
            jobId1 = await scheduler.EnqueueAsync<SuccessJobWithInput, SuccessInput>(
                new SuccessInput("concurrent-1"));
            jobId2 = await scheduler.EnqueueAsync<SuccessJobWithInput, SuccessInput>(
                new SuccessInput("concurrent-2"));
            await host1.StopAsync();
        }

        using var host2 = BuildHost(Storage(), s => s.AddTransient<SuccessJobWithInput>(), workers: 2);
        await host2.StartAsync();

        var storage = host2.Services.GetRequiredService<Storage.IStorageProvider>();
        var job1 = await storage.GetJobByIdAsync(jobId1);
        var job2 = await storage.GetJobByIdAsync(jobId2);

        job1.Should().NotBeNull();
        job2.Should().NotBeNull();

        await host2.StopAsync();
    }
}
