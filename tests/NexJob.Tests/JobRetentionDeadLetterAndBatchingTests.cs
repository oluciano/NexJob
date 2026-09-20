using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob;
using NexJob.Configuration;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Unit tests for dead-letter retention policy and batched/chunked purging.
/// Covers the mandatory 3N testing matrix (Positive, Negative, Boundary).
/// </summary>
public sealed class JobRetentionDeadLetterAndBatchingTests
{
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
        CreatedAt = completedAt.AddMinutes(-10),
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

    // ─── N1: Positive Tests ──────────────────────────────────────────────────

    /// <summary>
    /// N1: Positive — dead-letter jobs older than RetainDeadLetter are purged.
    /// </summary>
    [Fact]
    public async Task PurgeJobsAsync_DeletesDeadLetterJobsOlderThanRetention()
    {
        var storage = new InMemoryStorageProvider();
        var now = DateTimeOffset.UtcNow;

        // Job failed 65 days ago (beyond default 60-day dead-letter retention)
        var oldJob = MakeFailedJob(now.AddDays(-65));
        await storage.EnqueueAsync(oldJob);

        var policy = new RetentionPolicy
        {
            RetainFailed = TimeSpan.Zero,
            RetainDeadLetter = TimeSpan.FromDays(60),
        };

        var deleted = await storage.PurgeJobsAsync(policy);

        deleted.Should().Be(1);
        var remaining = await storage.GetJobByIdAsync(oldJob.Id);
        remaining.Should().BeNull();
    }

    /// <summary>
    /// N1: Positive — batched chunked purging deletes all eligible jobs across multiple chunks.
    /// </summary>
    [Fact]
    public async Task PurgeJobsAsync_BatchedPurging_DeletesAllEligibleJobsAcrossChunks()
    {
        var storage = new InMemoryStorageProvider();
        var now = DateTimeOffset.UtcNow;

        // Create 12 expired jobs
        for (var i = 0; i < 12; i++)
        {
            var job = MakeExpiredJob(now.AddDays(-10));
            await storage.EnqueueAsync(job);
        }

        // Batch size of 5: should run 3 chunks (5 + 5 + 2)
        var policy = new RetentionPolicy
        {
            RetainExpired = TimeSpan.FromDays(7),
            BatchSize = 5,
        };

        var deleted = await storage.PurgeJobsAsync(policy);

        deleted.Should().Be(12);
    }

    /// <summary>
    /// N1: Positive — mixed terminal jobs older than respective retention periods are all purged.
    /// </summary>
    [Fact]
    public async Task PurgeJobsAsync_MixedTerminalJobs_DeletesAllPastThresholds()
    {
        var storage = new InMemoryStorageProvider();
        var now = DateTimeOffset.UtcNow;

        var deadLetterOld = MakeFailedJob(now.AddDays(-70));
        var expiredOld = MakeExpiredJob(now.AddDays(-10));

        await storage.EnqueueAsync(deadLetterOld);
        await storage.EnqueueAsync(expiredOld);

        var policy = new RetentionPolicy
        {
            RetainDeadLetter = TimeSpan.FromDays(60),
            RetainExpired = TimeSpan.FromDays(7),
            BatchSize = 10,
        };

        var deleted = await storage.PurgeJobsAsync(policy);

        deleted.Should().Be(2);
        (await storage.GetJobByIdAsync(deadLetterOld.Id)).Should().BeNull();
        (await storage.GetJobByIdAsync(expiredOld.Id)).Should().BeNull();
    }

    /// <summary>
    /// N1: Positive — JobRetentionService correctly forwards RetentionDeadLetter and RetentionBatchSize to RetentionPolicy.
    /// </summary>
    [Fact]
    public async Task JobRetentionService_PassesRetentionDeadLetterAndBatchSizeToPolicy()
    {
        var storage = new Mock<IJobStorage>();
        var runtimeStore = new Mock<IRuntimeSettingsStore>();

        var options = new NexJobOptions
        {
            RetentionInterval = TimeSpan.FromMilliseconds(10),
            RetentionDeadLetter = TimeSpan.FromDays(45),
            RetentionBatchSize = 2500,
        };

        var runtime = new RuntimeSettings
        {
            RetentionDeadLetter = TimeSpan.FromDays(50),
            RetentionBatchSize = 3000,
        };

        runtimeStore.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(runtime);
        storage.Setup(x => x.PurgeJobsAsync(It.IsAny<RetentionPolicy>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var sut = new JobRetentionService(storage.Object, runtimeStore.Object, options, NullLogger<JobRetentionService>.Instance);
        using var cts = new CancellationTokenSource();

        _ = sut.StartAsync(cts.Token);
        await Task.Delay(50, CancellationToken.None);
        await sut.StopAsync(CancellationToken.None);

        storage.Verify(x => x.PurgeJobsAsync(
            It.Is<RetentionPolicy>(p =>
                p.RetainDeadLetter == TimeSpan.FromDays(50) &&
                p.BatchSize == 3000),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ─── N2: Negative Tests ──────────────────────────────────────────────────

    /// <summary>
    /// N2: Negative — dead-letter jobs within RetainDeadLetter threshold are preserved.
    /// </summary>
    [Fact]
    public async Task PurgeJobsAsync_PreservesDeadLetterJobsWithinRetention()
    {
        var storage = new InMemoryStorageProvider();
        var now = DateTimeOffset.UtcNow;

        // Job failed 20 days ago (within 60-day dead-letter retention)
        var recentJob = MakeFailedJob(now.AddDays(-20));
        await storage.EnqueueAsync(recentJob);

        var policy = new RetentionPolicy
        {
            RetainFailed = TimeSpan.Zero,
            RetainDeadLetter = TimeSpan.FromDays(60),
        };

        var deleted = await storage.PurgeJobsAsync(policy);

        deleted.Should().Be(0);
        var remaining = await storage.GetJobByIdAsync(recentJob.Id);
        remaining.Should().NotBeNull();
    }

    /// <summary>
    /// N2: Negative — setting RetainDeadLetter to TimeSpan.Zero disables dead-letter purging.
    /// </summary>
    [Fact]
    public async Task PurgeJobsAsync_WhenRetainDeadLetterIsZero_SkipsDeadLetterJobs()
    {
        var storage = new InMemoryStorageProvider();
        var now = DateTimeOffset.UtcNow;

        // Job failed 100 days ago
        var oldJob = MakeFailedJob(now.AddDays(-100));
        await storage.EnqueueAsync(oldJob);

        var policy = new RetentionPolicy
        {
            RetainFailed = TimeSpan.Zero,
            RetainDeadLetter = TimeSpan.Zero,
        };

        var deleted = await storage.PurgeJobsAsync(policy);

        deleted.Should().Be(0);
        var remaining = await storage.GetJobByIdAsync(oldJob.Id);
        remaining.Should().NotBeNull();
    }

    /// <summary>
    /// N2: Negative — cancelled CancellationToken halts batched purge loop gracefully.
    /// </summary>
    [Fact]
    public async Task PurgeJobsAsync_WhenCancelled_HaltsPurgeLoopGracefully()
    {
        var storage = new InMemoryStorageProvider();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 10; i++)
        {
            var job = MakeExpiredJob(now.AddDays(-10));
            await storage.EnqueueAsync(job);
        }

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // Pre-cancelled token

        var policy = new RetentionPolicy
        {
            RetainExpired = TimeSpan.FromDays(7),
            BatchSize = 2,
        };

        var deleted = await storage.PurgeJobsAsync(policy, cts.Token);

        deleted.Should().Be(0);
    }

    // ─── N3: Boundary Tests ──────────────────────────────────────────────────

    /// <summary>
    /// N3: Boundary — BatchSize. <= 0 defaults safely to 1000 without error or infinite loop.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-500)]
    public async Task PurgeJobsAsync_WithZeroOrNegativeBatchSize_DefaultsSafely(int invalidBatchSize)
    {
        var storage = new InMemoryStorageProvider();
        var now = DateTimeOffset.UtcNow;

        var job = MakeExpiredJob(now.AddDays(-10));
        await storage.EnqueueAsync(job);

        var policy = new RetentionPolicy
        {
            RetainExpired = TimeSpan.FromDays(7),
            BatchSize = invalidBatchSize,
        };

        var deleted = await storage.PurgeJobsAsync(policy);

        deleted.Should().Be(1);
    }

    /// <summary>
    /// N3: Boundary — calling PurgeJobsAsync on empty storage returns zero.
    /// </summary>
    [Fact]
    public async Task PurgeJobsAsync_EmptyStorage_ReturnsZero()
    {
        var storage = new InMemoryStorageProvider();

        var policy = new RetentionPolicy
        {
            RetainDeadLetter = TimeSpan.FromDays(60),
            RetainFailed = TimeSpan.FromDays(30),
            RetainSucceeded = TimeSpan.FromDays(7),
            RetainExpired = TimeSpan.FromDays(7),
            BatchSize = 100,
        };

        var deleted = await storage.PurgeJobsAsync(policy);

        deleted.Should().Be(0);
    }

    /// <summary>
    /// N3: Boundary — exactly BatchSize jobs are deleted in a single chunk and loop terminates cleanly.
    /// </summary>
    [Fact]
    public async Task PurgeJobsAsync_ExactlyBatchSizeJobs_TerminatesCleanly()
    {
        var storage = new InMemoryStorageProvider();
        var now = DateTimeOffset.UtcNow;

        for (var i = 0; i < 5; i++)
        {
            var job = MakeExpiredJob(now.AddDays(-10));
            await storage.EnqueueAsync(job);
        }

        var policy = new RetentionPolicy
        {
            RetainExpired = TimeSpan.FromDays(7),
            BatchSize = 5,
        };

        var deleted = await storage.PurgeJobsAsync(policy);

        deleted.Should().Be(5);
    }
}
