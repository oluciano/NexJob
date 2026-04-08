using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NexJob;
using NexJob.Configuration;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

public sealed class JobRetentionServiceTests
{
    private static JobRecord MakeSucceededJob(DateTimeOffset completedAt) => new()
    {
        Id = JobId.New(),
        JobType = "FakeJob",
        InputType = "System.String",
        InputJson = "\"test\"",
        Queue = "default",
        Priority = JobPriority.Normal,
        Status = JobStatus.Succeeded,
        CompletedAt = completedAt,
        Attempts = 1,
        MaxAttempts = 5,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
    };

    private static JobRecord MakeFailedJob(DateTimeOffset completedAt) => new()
    {
        Id = JobId.New(),
        JobType = "FakeJob",
        InputType = "System.String",
        InputJson = "\"test\"",
        Queue = "default",
        Priority = JobPriority.Normal,
        Status = JobStatus.Failed,
        CompletedAt = completedAt,
        Attempts = 5,
        MaxAttempts = 5,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
    };

    private static JobRecord MakeExpiredJob(DateTimeOffset createdAt) => new()
    {
        Id = JobId.New(),
        JobType = "FakeJob",
        InputType = "System.String",
        InputJson = "\"test\"",
        Queue = "default",
        Priority = JobPriority.Normal,
        Status = JobStatus.Expired,
        Attempts = 0,
        MaxAttempts = 5,
        CreatedAt = createdAt,
    };

    [Fact]
    public async Task PurgeJobsAsync_DeletesSucceededJobsOlderThanRetention()
    {
        var storage = new InMemoryStorageProvider();
        var now = DateTimeOffset.UtcNow;

        // Job completed 8 days ago (beyond default 7-day retention)
        var oldJob = MakeSucceededJob(now.AddDays(-8));
        await storage.EnqueueAsync(oldJob);

        var policy = new RetentionPolicy { RetainSucceeded = TimeSpan.FromDays(7), };

        var deleted = await storage.PurgeJobsAsync(policy);

        deleted.Should().Be(1);
        var remaining = await storage.GetJobByIdAsync(oldJob.Id);
        remaining.Should().BeNull();
    }

    [Fact]
    public async Task PurgeJobsAsync_PreservesSucceededJobsWithinRetention()
    {
        var storage = new InMemoryStorageProvider();
        var now = DateTimeOffset.UtcNow;

        // Job completed 3 days ago (within default 7-day retention)
        var recentJob = MakeSucceededJob(now.AddDays(-3));
        await storage.EnqueueAsync(recentJob);

        var policy = new RetentionPolicy { RetainSucceeded = TimeSpan.FromDays(7), };

        var deleted = await storage.PurgeJobsAsync(policy);

        deleted.Should().Be(0);
        var remaining = await storage.GetJobByIdAsync(recentJob.Id);
        remaining.Should().NotBeNull();
    }

    [Fact]
    public async Task PurgeJobsAsync_DeletesFailedJobsOlderThanRetention()
    {
        var storage = new InMemoryStorageProvider();
        var now = DateTimeOffset.UtcNow;

        // Job failed 35 days ago (beyond default 30-day retention)
        var oldJob = MakeFailedJob(now.AddDays(-35));
        await storage.EnqueueAsync(oldJob);

        var policy = new RetentionPolicy { RetainFailed = TimeSpan.FromDays(30), };

        var deleted = await storage.PurgeJobsAsync(policy);

        deleted.Should().Be(1);
        var remaining = await storage.GetJobByIdAsync(oldJob.Id);
        remaining.Should().BeNull();
    }

    [Fact]
    public async Task PurgeJobsAsync_DeletesExpiredJobsUsingCreatedAt()
    {
        var storage = new InMemoryStorageProvider();
        var now = DateTimeOffset.UtcNow;

        // Job created 8 days ago (beyond default 7-day retention)
        var oldJob = MakeExpiredJob(now.AddDays(-8));
        await storage.EnqueueAsync(oldJob);

        var policy = new RetentionPolicy { RetainExpired = TimeSpan.FromDays(7), };

        var deleted = await storage.PurgeJobsAsync(policy);

        deleted.Should().Be(1);
        var remaining = await storage.GetJobByIdAsync(oldJob.Id);
        remaining.Should().BeNull();
    }

    [Fact]
    public async Task PurgeJobsAsync_WhenRetainSucceededIsZero_SkipsSucceeded()
    {
        var storage = new InMemoryStorageProvider();
        var now = DateTimeOffset.UtcNow;

        // Old succeeded job
        var oldJob = MakeSucceededJob(now.AddDays(-100));
        await storage.EnqueueAsync(oldJob);

        var policy = new RetentionPolicy
        {
            RetainSucceeded = TimeSpan.Zero,
            RetainFailed = TimeSpan.Zero,
            RetainExpired = TimeSpan.Zero,
        };

        var deleted = await storage.PurgeJobsAsync(policy);

        deleted.Should().Be(0);
        var remaining = await storage.GetJobByIdAsync(oldJob.Id);
        remaining.Should().NotBeNull();
    }

    [Fact]
    public async Task PurgeJobsAsync_NeverDeletesProcessingJobs()
    {
        var storage = new InMemoryStorageProvider();

        var processingJob = new JobRecord
        {
            Id = JobId.New(),
            JobType = "FakeJob",
            InputType = "System.String",
            InputJson = "\"test\"",
            Queue = "default",
            Priority = JobPriority.Normal,
            Status = JobStatus.Processing,
            Attempts = 1,
            MaxAttempts = 5,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-100),
        };
        await storage.EnqueueAsync(processingJob);

        var policy = new RetentionPolicy
        {
            RetainSucceeded = TimeSpan.FromDays(1),
            RetainFailed = TimeSpan.FromDays(1),
            RetainExpired = TimeSpan.FromDays(1),
        };

        var deleted = await storage.PurgeJobsAsync(policy);

        deleted.Should().Be(0);
        var remaining = await storage.GetJobByIdAsync(processingJob.Id);
        remaining.Should().NotBeNull();
    }

    [Fact]
    public async Task PurgeJobsAsync_ReturnsCorrectDeleteCount()
    {
        var storage = new InMemoryStorageProvider();
        var now = DateTimeOffset.UtcNow;

        // 3 old succeeded jobs
        for (var i = 0; i < 3; i++)
        {
            var job = MakeSucceededJob(now.AddDays(-10));
            await storage.EnqueueAsync(job);
        }

        // 2 old failed jobs
        for (var i = 0; i < 2; i++)
        {
            var job = MakeFailedJob(now.AddDays(-40));
            await storage.EnqueueAsync(job);
        }

        // 1 recent succeeded job (should not be deleted)
        var recentJob = MakeSucceededJob(now.AddDays(-1));
        await storage.EnqueueAsync(recentJob);

        var policy = new RetentionPolicy
        {
            RetainSucceeded = TimeSpan.FromDays(7),
            RetainFailed = TimeSpan.FromDays(30),
            RetainExpired = TimeSpan.FromDays(7),
        };

        var deleted = await storage.PurgeJobsAsync(policy);

        deleted.Should().Be(5);
        var remaining = await storage.GetJobByIdAsync(recentJob.Id);
        remaining.Should().NotBeNull();
    }
}
