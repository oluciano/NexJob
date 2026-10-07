using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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

    private readonly Mock<IJobStorage> _storage = new();
    private readonly Mock<IRuntimeSettingsStore> _runtimeStore = new();
    private readonly NexJobOptions _options = new()
    {
        RetentionInterval = TimeSpan.FromMilliseconds(10),
        RetentionSucceeded = TimeSpan.FromDays(7),
        RetentionFailed = TimeSpan.FromDays(30),
        RetentionExpired = TimeSpan.FromDays(7),
    };

    /// <summary>Tests that background service executes purge and survives storage errors.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteAsync_RunsPurgeAndSurvivesErrors()
    {
        // Arrange
        _runtimeStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new RuntimeSettings());

        // Sequence: 1. Throws error, 2. Returns 5 deleted jobs, 3. Returns 0 deleted jobs
        _storage.SetupSequence(x => x.PurgeJobsAsync(It.IsAny<RetentionPolicy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Storage failure"))
            .ReturnsAsync(5)
            .ReturnsAsync(0);

        var sut = new JobRetentionService(_storage.Object, _runtimeStore.Object, _options, NullLogger<JobRetentionService>.Instance);
        using var cts = new CancellationTokenSource();

        // Act
        _ = sut.StartAsync(cts.Token);
        await Task.Delay(100, CancellationToken.None); // Allow multiple cycles
        await sut.StopAsync(CancellationToken.None);

        // Assert
        _storage.Verify(x => x.PurgeJobsAsync(It.IsAny<RetentionPolicy>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    /// <summary>Tests that runtime overrides are respected during purge.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteAsync_RespectsRuntimeOverrides()
    {
        // Arrange
        var runtime = new RuntimeSettings
        {
            RetentionSucceeded = TimeSpan.FromDays(1),
            RetentionFailed = TimeSpan.FromDays(2),
            RetentionExpired = TimeSpan.FromDays(3),
        };
        _runtimeStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(runtime);
        _storage.Setup(x => x.PurgeJobsAsync(It.IsAny<RetentionPolicy>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var sut = new JobRetentionService(_storage.Object, _runtimeStore.Object, _options, NullLogger<JobRetentionService>.Instance);
        using var cts = new CancellationTokenSource();

        // Act
        _ = sut.StartAsync(cts.Token);
        await Task.Delay(50, CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        // Assert
        _storage.Verify(x => x.PurgeJobsAsync(
            It.Is<RetentionPolicy>(p =>
                p.RetainSucceeded == runtime.RetentionSucceeded &&
                p.RetainFailed == runtime.RetentionFailed &&
                p.RetainExpired == runtime.RetentionExpired),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }
}
