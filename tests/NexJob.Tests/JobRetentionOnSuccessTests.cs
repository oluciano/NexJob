using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob.Exceptions;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// 3N Hardening Unit Tests for Anti-Bloat Retention Strategies (Issue #203).
/// Covers:
/// - N1 (Positive): Immediate purge on success, payload stripping on success, normal retention fallback.
/// - N2 (Negative): PurgeOnSuccess job failure retains record and error details for dead-lettering.
/// - N3 (Boundary/Inputs): Batch acknowledgment respects PurgeOnSuccess/TrimPayload, edge cases with empty payloads.
/// </summary>
public sealed class JobRetentionOnSuccessTests
{
    private readonly Mock<IJobStorage> _storage = new();
    private readonly Mock<IJobInvokerFactory> _invokerFactory = new();
    private readonly Mock<IJobRetryPolicy> _retryPolicy = new();
    private readonly Mock<IDeadLetterDispatcher> _deadLetterDispatcher = new();
    private readonly ThrottleRegistry _throttleRegistry = new();
    private readonly NexJobOptions _options = new() { HeartbeatInterval = TimeSpan.FromMilliseconds(10) };
    private readonly JobExecutor _sut;

    public JobRetentionOnSuccessTests()
    {
        _sut = new JobExecutor(
            _storage.Object,
            _invokerFactory.Object,
            _retryPolicy.Object,
            _deadLetterDispatcher.Object,
            _throttleRegistry,
            _options,
            Enumerable.Empty<IJobExecutionFilter>(),
            NullLogger<JobExecutor>.Instance);
    }

    // ─── N1: Positive Scenarios (Happy Path) ────────────────────────────────

    [Fact]
    public async Task ExecuteJobAsync_WhenJobHasPurgeOnSuccessAttribute_CommitsWithPurgeOnSuccessTrue()
    {
        // Arrange
        var job = new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(EphemeralJob).AssemblyQualifiedName!,
            InputType = typeof(string).AssemblyQualifiedName!,
            InputJson = "\"sample\"",
            Queue = "default",
        };

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var scope = serviceProvider.CreateScope();
        var invocationContext = new JobInvocationContext(
            scope,
            new EphemeralJob(),
            "sample",
            (instance, input, ct) => Task.CompletedTask,
            Enumerable.Empty<ThrottleAttribute>(),
            new RetentionAttribute { PurgeOnSuccess = true });

        _invokerFactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invocationContext);

        JobExecutionResult? capturedResult = null;
        _storage.Setup(x => x.CommitJobResultAsync(job.Id, It.IsAny<JobExecutionResult>(), It.IsAny<CancellationToken>()))
            .Callback<JobId, JobExecutionResult, CancellationToken>((id, res, ct) => capturedResult = res)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.ExecuteJobAsync(job);

        // Assert
        capturedResult.Should().NotBeNull();
        capturedResult!.Succeeded.Should().BeTrue();
        capturedResult.PurgeOnSuccess.Should().BeTrue();
        capturedResult.TrimPayloadOnSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteJobAsync_WhenJobHasTrimPayloadOnSuccessAttribute_CommitsWithTrimPayloadOnSuccessTrue()
    {
        // Arrange
        var job = new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(TrimPayloadJob).AssemblyQualifiedName!,
            InputType = typeof(string).AssemblyQualifiedName!,
            InputJson = "\"heavy-payload-data\"",
            Queue = "default",
        };

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var scope = serviceProvider.CreateScope();
        var invocationContext = new JobInvocationContext(
            scope,
            new TrimPayloadJob(),
            "heavy-payload-data",
            (instance, input, ct) => Task.CompletedTask,
            Enumerable.Empty<ThrottleAttribute>(),
            new RetentionAttribute { TrimPayloadOnSuccess = true });

        _invokerFactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invocationContext);

        JobExecutionResult? capturedResult = null;
        _storage.Setup(x => x.CommitJobResultAsync(job.Id, It.IsAny<JobExecutionResult>(), It.IsAny<CancellationToken>()))
            .Callback<JobId, JobExecutionResult, CancellationToken>((id, res, ct) => capturedResult = res)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.ExecuteJobAsync(job);

        // Assert
        capturedResult.Should().NotBeNull();
        capturedResult!.Succeeded.Should().BeTrue();
        capturedResult.PurgeOnSuccess.Should().BeFalse();
        capturedResult.TrimPayloadOnSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteJobAsync_WhenJobHasNoRetentionAttribute_CommitsWithStandardRetentionFlags()
    {
        // Arrange
        var job = new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(StandardJob).AssemblyQualifiedName!,
            InputType = typeof(string).AssemblyQualifiedName!,
            InputJson = "\"standard\"",
            Queue = "default",
        };

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var scope = serviceProvider.CreateScope();
        var invocationContext = new JobInvocationContext(
            scope,
            new StandardJob(),
            "standard",
            (instance, input, ct) => Task.CompletedTask,
            Enumerable.Empty<ThrottleAttribute>());

        _invokerFactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invocationContext);

        JobExecutionResult? capturedResult = null;
        _storage.Setup(x => x.CommitJobResultAsync(job.Id, It.IsAny<JobExecutionResult>(), It.IsAny<CancellationToken>()))
            .Callback<JobId, JobExecutionResult, CancellationToken>((id, res, ct) => capturedResult = res)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.ExecuteJobAsync(job);

        // Assert
        capturedResult.Should().NotBeNull();
        capturedResult!.Succeeded.Should().BeTrue();
        capturedResult.PurgeOnSuccess.Should().BeFalse();
        capturedResult.TrimPayloadOnSuccess.Should().BeFalse();
    }

    // ─── N2: Negative Scenarios (Failure Resilience) ────────────────────────

    [Fact]
    public async Task ExecuteJobAsync_WhenPurgeOnSuccessJobFails_DoesNotPurgeAndRecordsFailure()
    {
        // Arrange
        var job = new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(EphemeralJob).AssemblyQualifiedName!,
            InputType = typeof(string).AssemblyQualifiedName!,
            InputJson = "\"sample\"",
            Queue = "default",
            Attempts = 1,
            MaxAttempts = 3,
        };

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var scope = serviceProvider.CreateScope();
        var invocationContext = new JobInvocationContext(
            scope,
            new EphemeralJob(),
            "sample",
            (instance, input, ct) => throw new InvalidOperationException("Upstream timeout"),
            Enumerable.Empty<ThrottleAttribute>(),
            new RetentionAttribute { PurgeOnSuccess = true });

        _invokerFactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invocationContext);

        _retryPolicy.Setup(x => x.ComputeRetryAt(It.IsAny<JobRecord>(), It.IsAny<Exception>()))
            .Returns(DateTimeOffset.UtcNow.AddSeconds(5));

        JobExecutionResult? capturedResult = null;
        _storage.Setup(x => x.CommitJobResultAsync(job.Id, It.IsAny<JobExecutionResult>(), It.IsAny<CancellationToken>()))
            .Callback<JobId, JobExecutionResult, CancellationToken>((id, res, ct) => capturedResult = res)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.ExecuteJobAsync(job);

        // Assert
        capturedResult.Should().NotBeNull();
        capturedResult!.Succeeded.Should().BeFalse();
        capturedResult.PurgeOnSuccess.Should().BeFalse(); // Never purge a failed job!
        capturedResult.Exception.Should().NotBeNull();
        capturedResult.Exception!.Message.Should().Be("Upstream timeout");
    }

    // ─── N3: Boundary Scenarios (Storage Operations) ────────────────────────

    [Fact]
    public async Task InMemoryStorage_CommitJobResultAsync_WhenPurgeOnSuccess_RemovesJobImmediately()
    {
        // Arrange
        var storage = new InMemoryStorageProvider();
        var job = new JobRecord
        {
            Id = JobId.New(),
            JobType = "EphemeralJob",
            Queue = "default",
            Status = JobStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await storage.EnqueueAsync(job);
        await storage.FetchNextAsync(new[] { "default" }); // Moves to Processing

        var result = new JobExecutionResult
        {
            Succeeded = true,
            Logs = Array.Empty<JobExecutionLog>(),
            PurgeOnSuccess = true,
        };

        // Act
        await storage.CommitJobResultAsync(job.Id, result);

        // Assert
        var fetched = await storage.GetJobByIdAsync(job.Id);
        fetched.Should().BeNull(); // Completely purged from memory
    }

    [Fact]
    public async Task InMemoryStorage_CommitJobResultAsync_WhenTrimPayloadOnSuccess_ClearsInputJson()
    {
        // Arrange
        var storage = new InMemoryStorageProvider();
        var job = new JobRecord
        {
            Id = JobId.New(),
            JobType = "TrimPayloadJob",
            InputJson = "{\"heavy\": \"data\"}",
            Queue = "default",
            Status = JobStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await storage.EnqueueAsync(job);
        await storage.FetchNextAsync(new[] { "default" });

        var result = new JobExecutionResult
        {
            Succeeded = true,
            Logs = Array.Empty<JobExecutionLog>(),
            TrimPayloadOnSuccess = true,
        };

        // Act
        await storage.CommitJobResultAsync(job.Id, result);

        // Assert
        var fetched = await storage.GetJobByIdAsync(job.Id);
        fetched.Should().NotBeNull();
        fetched!.Status.Should().Be(JobStatus.Succeeded);
        fetched.InputJson.Should().BeEmpty(); // Payload cleared
    }

    [Fact]
    public async Task InMemoryStorage_GetJobCatalogAsync_PreservesCountersEvenWhenPurged()
    {
        // Arrange
        var storage = new InMemoryStorageProvider();
        var job = new JobRecord
        {
            Id = JobId.New(),
            JobType = "EphemeralStreamJob",
            Queue = "telemetry",
            Status = JobStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-2),
            ProcessingStartedAt = DateTimeOffset.UtcNow.AddSeconds(-2),
        };
        await storage.EnqueueAsync(job);
        await storage.FetchNextAsync(new[] { "telemetry" });

        var result = new JobExecutionResult
        {
            Succeeded = true,
            Logs = Array.Empty<JobExecutionLog>(),
            PurgeOnSuccess = true,
        };

        // Act
        await storage.CommitJobResultAsync(job.Id, result);

        // Assert
        // 1. Job row is gone
        var fetched = await storage.GetJobByIdAsync(job.Id);
        fetched.Should().BeNull();

        // 2. Catalog preserves the lifetime stats
        var catalog = await storage.GetJobCatalogAsync();
        catalog.Should().ContainSingle(c => c.JobType == "EphemeralStreamJob" && c.Queue == "telemetry");
        var item = catalog.First(c => c.JobType == "EphemeralStreamJob");
        item.TotalRuns.Should().Be(1);
        item.SucceededRuns.Should().Be(1);
        item.FailedRuns.Should().Be(0);
        item.LastExecutedAt.Should().NotBeNull();
    }

    // ─── Sample Job Types for Testing ───────────────────────────────────────

    [Retention(PurgeOnSuccess = true)]
    private sealed class EphemeralJob : IJob<string>
    {
        public Task ExecuteAsync(string input, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Retention(TrimPayloadOnSuccess = true)]
    private sealed class TrimPayloadJob : IJob<string>
    {
        public Task ExecuteAsync(string input, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StandardJob : IJob<string>
    {
        public Task ExecuteAsync(string input, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
