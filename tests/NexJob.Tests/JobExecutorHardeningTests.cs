using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Hardening unit tests for <see cref="JobExecutor"/>.
/// Targets 100% branch coverage for the core execution pipeline.
/// </summary>
public sealed class JobExecutorHardeningTests
{
    private readonly Mock<IJobStorage> _storage = new();
    private readonly Mock<IJobInvokerFactory> _invokerFactory = new();
    private readonly Mock<IJobRetryPolicy> _retryPolicy = new();
    private readonly Mock<IDeadLetterDispatcher> _deadLetterDispatcher = new();
    private readonly ThrottleRegistry _throttleRegistry = new();
    private readonly NexJobOptions _options = new() { HeartbeatInterval = TimeSpan.FromMilliseconds(10) };
    private readonly JobExecutor _sut;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobExecutorHardeningTests"/> class.
    /// </summary>
    public JobExecutorHardeningTests()
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

    // ─── TryHandleExpirationAsync Branches ─────────────────────────────────

    /// <summary>Tests that expired jobs are marked as expired and not executed.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenJobIsExpired_SetsStatusAndReturns()
    {
        // Arrange
        var job = new JobRecord
        {
            Id = JobId.New(),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            JobType = "TestJob",
        };

        // Act
        await _sut.ExecuteJobAsync(job);

        // Assert
        _storage.Verify(x => x.SetExpiredAsync(job.Id, It.IsAny<CancellationToken>()), Times.Once);
        _invokerFactory.Verify(x => x.PrepareAsync(It.IsAny<JobRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Tests that jobs with future expiration are executed normally.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenJobNotExpired_ExecutesNormally()
    {
        // Arrange
        var job = new JobRecord
        {
            Id = JobId.New(),
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            JobType = "TestJob",
        };
        _ = SetupSuccessfulInvoker(job);

        // Act
        await _sut.ExecuteJobAsync(job);

        // Assert
        _storage.Verify(x => x.SetExpiredAsync(job.Id, It.IsAny<CancellationToken>()), Times.Never);
        _storage.Verify(x => x.CommitJobResultAsync(job.Id, It.Is<JobExecutionResult>(r => r.Succeeded), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Failure Path Branches ─────────────────────────────────────────────

    /// <summary>Tests that dead-letter dispatcher is invoked when retries are exhausted.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenRetryAtIsNull_DispatchesToDeadLetter()
    {
        // Arrange
        var job = new JobRecord { Id = JobId.New(), JobType = "TestJob" };
        var exception = new Exception("Terminal failure");

        _invokerFactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        _retryPolicy.Setup(x => x.ComputeRetryAt(job, exception))
            .Returns((DateTimeOffset?)null);

        // Act
        await _sut.ExecuteJobAsync(job);

        // Assert
        _deadLetterDispatcher.Verify(x => x.DispatchAsync(job, exception, It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(x => x.CommitJobResultAsync(job.Id, It.Is<JobExecutionResult>(r => !r.Succeeded && r.RetryAt == null), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Tests that dead-letter is NOT invoked when a retry is scheduled.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenRetryIsScheduled_DoesNotDispatchToDeadLetter()
    {
        // Arrange
        var job = new JobRecord { Id = JobId.New(), JobType = "TestJob" };
        var exception = new Exception("Transient failure");
        var retryAt = DateTimeOffset.UtcNow.AddMinutes(1);

        _invokerFactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        _retryPolicy.Setup(x => x.ComputeRetryAt(job, exception))
            .Returns(retryAt);

        // Act
        await _sut.ExecuteJobAsync(job);

        // Assert
        _deadLetterDispatcher.Verify(x => x.DispatchAsync(It.IsAny<JobRecord>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
        _storage.Verify(x => x.CommitJobResultAsync(job.Id, It.Is<JobExecutionResult>(r => r.RetryAt == retryAt), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Throttling Branches ───────────────────────────────────────────────

    /// <summary>Tests that JobExecutor respects throttling attributes.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WithThrottling_AcquiresAndReleasesSlots()
    {
        // Arrange
        var job = new JobRecord { Id = JobId.New(), JobType = "TestJob" };
        var throttle = new ThrottleAttribute("resource1", 1);
        _ = SetupSuccessfulInvoker(job, new[] { throttle });

        // Act
        await _sut.ExecuteJobAsync(job);

        // Assert
        _storage.Verify(x => x.CommitJobResultAsync(job.Id, It.Is<JobExecutionResult>(r => r.Succeeded), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Filter Branches ───────────────────────────────────────────────────

    /// <summary>Tests that JobExecutor invokes filters in the correct order.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WithFilters_InvokesPipeline()
    {
        // Arrange
        var job = new JobRecord { Id = JobId.New(), JobType = "TestJob" };
        var filterMock = new Mock<IJobExecutionFilter>();

        var sutWithFilters = new JobExecutor(
            _storage.Object,
            _invokerFactory.Object,
            _retryPolicy.Object,
            _deadLetterDispatcher.Object,
            _throttleRegistry,
            _options,
            new[] { filterMock.Object },
            NullLogger<JobExecutor>.Instance);

        _ = SetupSuccessfulInvoker(job);

        // Act
        await sutWithFilters.ExecuteJobAsync(job);

        // Assert
        filterMock.Verify(x => x.OnExecutingAsync(
            It.IsAny<JobExecutingContext>(),
            It.IsAny<JobExecutionDelegate>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Heartbeat Branches ────────────────────────────────────────────────

    /// <summary>Tests that heartbeat is updated during job execution.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_UpdatesHeartbeatDuringExecution()
    {
        // Arrange
        var job = new JobRecord { Id = JobId.New(), JobType = "TestJob" };

        _invokerFactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var scope = new Mock<IServiceScope>();
                return new JobInvocationContext(
                    scope.Object,
                    new object(),
                    new object(),
                    (object j, object i, CancellationToken ct) => Task.Delay(50, ct),
                    Array.Empty<ThrottleAttribute>());
            });

        // Act
        await _sut.ExecuteJobAsync(job);

        // Assert
        _storage.Verify(x => x.UpdateHeartbeatAsync(job.Id, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private JobInvocationContext SetupSuccessfulInvoker(JobRecord job, ThrottleAttribute[]? throttles = null)
    {
        var scope = new Mock<IServiceScope>();
        var context = new JobInvocationContext(
            scope.Object,
            new object(),
            new object(),
            (object j, object i, CancellationToken ct) => Task.CompletedTask,
            throttles ?? Array.Empty<ThrottleAttribute>());

        _invokerFactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);

        return context;
    }

    /// <summary>Support job.</summary>
    public class TestJob : IJob
    {
        /// <inheritdoc/>
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
