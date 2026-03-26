using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NexJob;
using NexJob.Internal;
using Xunit;

namespace NexJob.Tests;

public sealed class OrphanedJobWatcherServiceTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static OrphanedJobWatcherService MakeService(
        InMemoryStorageProvider storage,
        TimeSpan? heartbeatTimeout = null) =>
        new OrphanedJobWatcherService(
            storage,
            new NexJobOptions { HeartbeatTimeout = heartbeatTimeout ?? TimeSpan.FromMilliseconds(5) },
            NullLogger<OrphanedJobWatcherService>.Instance);

    /// <summary>
    /// Creates a job already in Processing state with the given heartbeat.
    /// Bypasses FetchNextAsync so the heartbeat can be set to any desired value.
    /// </summary>
    private static JobRecord MakeProcessingJob(DateTimeOffset? heartbeatAt = null) => new()
    {
        Id = JobId.New(),
        JobType = "FakeJob",
        InputType = "System.String",
        InputJson = "\"test\"",
        Queue = "default",
        Priority = JobPriority.Normal,
        Status = JobStatus.Processing,
        HeartbeatAt = heartbeatAt,
        Attempts = 1,
        MaxAttempts = 5,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    // ─── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OrphanedJob_NullHeartbeat_IsReenqueued()
    {
        var storage = new InMemoryStorageProvider();
        var job = MakeProcessingJob(heartbeatAt: null);
        await storage.EnqueueAsync(job);

        var svc = (IHostedService)MakeService(storage, heartbeatTimeout: TimeSpan.FromMilliseconds(5));
        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await svc.StopAsync(CancellationToken.None);

        var requeued = await storage.FetchNextAsync(["default"]);
        requeued.Should().NotBeNull("job with null heartbeat should be requeued as orphaned");
        requeued!.Id.Should().Be(job.Id);
        requeued.Status.Should().Be(JobStatus.Processing); // now claimed by us
    }

    [Fact]
    public async Task OrphanedJob_ExpiredHeartbeat_IsReenqueued()
    {
        var storage = new InMemoryStorageProvider();
        var staleHeartbeat = DateTimeOffset.UtcNow.AddMinutes(-10);
        var job = MakeProcessingJob(heartbeatAt: staleHeartbeat);
        await storage.EnqueueAsync(job);

        var svc = (IHostedService)MakeService(storage, heartbeatTimeout: TimeSpan.FromMilliseconds(5));
        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await svc.StopAsync(CancellationToken.None);

        var requeued = await storage.FetchNextAsync(["default"]);
        requeued.Should().NotBeNull("job with stale heartbeat should be requeued");
        requeued!.Id.Should().Be(job.Id);
    }

    [Fact]
    public async Task ActiveJob_FreshHeartbeat_IsNotReenqueued()
    {
        var storage = new InMemoryStorageProvider();
        // Heartbeat far in the future relative to the 5-minute timeout
        var freshHeartbeat = DateTimeOffset.UtcNow.AddMinutes(10);
        var job = MakeProcessingJob(heartbeatAt: freshHeartbeat);
        await storage.EnqueueAsync(job);

        var svc = (IHostedService)MakeService(storage, heartbeatTimeout: TimeSpan.FromMinutes(5));
        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await svc.StopAsync(CancellationToken.None);

        // Job should still be Processing — FetchNextAsync will return null because no Enqueued jobs
        var fetched = await storage.FetchNextAsync(["default"]);
        fetched.Should().BeNull("active job with fresh heartbeat must not be requeued");
    }

    [Fact]
    public async Task EnqueuedJob_IsNeverTreatedAsOrphaned()
    {
        var storage = new InMemoryStorageProvider();
        // An Enqueued job with no heartbeat — watcher should ignore it
        await storage.EnqueueAsync(new JobRecord
        {
            Id = JobId.New(),
            JobType = "FakeJob",
            InputType = "System.String",
            InputJson = "\"test\"",
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.Enqueued,
            HeartbeatAt = null,
            Attempts = 0,
            MaxAttempts = 5,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var svc = (IHostedService)MakeService(storage, heartbeatTimeout: TimeSpan.FromMilliseconds(5));
        await svc.StartAsync(CancellationToken.None);
        await Task.Delay(100);
        await svc.StopAsync(CancellationToken.None);

        // The job was Enqueued before; watcher should not duplicate it
        var first = await storage.FetchNextAsync(["default"]);
        var second = await storage.FetchNextAsync(["default"]);
        first.Should().NotBeNull();
        second.Should().BeNull("Enqueued job must not be duplicated by the orphan watcher");
    }
}
