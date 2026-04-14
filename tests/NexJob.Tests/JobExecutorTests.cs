using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

public sealed class JobExecutorTests
{
    private readonly Mock<IJobStorage> _storage = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly Mock<IServiceScope> _scope = new();
    private readonly Mock<IServiceProvider> _serviceProvider = new();
    private readonly Mock<IJobContextAccessor> _contextAccessor = new();
    private readonly Mock<IMigrationPipeline> _migrationPipeline = new();
    private readonly ThrottleRegistry _throttleRegistry = new();
    private readonly NexJobOptions _options = new();
    private readonly JobExecutor _sut;

    public JobExecutorTests()
    {
        _scopeFactory.Setup(x => x.CreateScope()).Returns(_scope.Object);
        _scope.Setup(x => x.ServiceProvider).Returns(_serviceProvider.Object);

        _serviceProvider.Setup(x => x.GetService(typeof(IJobContextAccessor))).Returns(_contextAccessor.Object);
        _serviceProvider.Setup(x => x.GetService(typeof(IMigrationPipeline))).Returns(_migrationPipeline.Object);

        _sut = new JobExecutor(
            _storage.Object,
            _scopeFactory.Object,
            _throttleRegistry,
            _options,
            Enumerable.Empty<IJobExecutionFilter>(),
            NullLogger<JobExecutor>.Instance);
    }

    [Fact]
    public async Task ExecuteJobAsync_SuccessfulJob_CommitsSucceeded()
    {
        // Arrange
        var job = MakeJob<TestJob, TestInput>(new TestInput("test"));
        var jobInstance = new TestJob();
        _serviceProvider.Setup(x => x.GetService(typeof(TestJob))).Returns(jobInstance);
        _migrationPipeline.Setup(x => x.Migrate(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Type>()))
            .Returns(job.InputJson);

        // Act
        await _sut.ExecuteJobAsync(job);

        // Assert
        _storage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => r.Succeeded),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteJobAsync_FailedJob_WithRetriesRemaining_ReEnqueues()
    {
        // Arrange
        var job = MakeJob<FailingJob, TestInput>(new TestInput("test"), maxAttempts: 3);
        job.Attempts = 1;
        var jobInstance = new FailingJob();
        _serviceProvider.Setup(x => x.GetService(typeof(FailingJob))).Returns(jobInstance);
        _migrationPipeline.Setup(x => x.Migrate(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Type>()))
            .Returns(job.InputJson);

        // Act
        await _sut.ExecuteJobAsync(job);

        // Assert
        _storage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded && r.RetryAt != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteJobAsync_FailedJob_NoRetriesRemaining_DeadLetters()
    {
        // Arrange
        var job = MakeJob<FailingJob, TestInput>(new TestInput("test"), maxAttempts: 1);
        job.Attempts = 1;
        var jobInstance = new FailingJob();
        _serviceProvider.Setup(x => x.GetService(typeof(FailingJob))).Returns(jobInstance);
        _migrationPipeline.Setup(x => x.Migrate(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Type>()))
            .Returns(job.InputJson);

        // Act
        await _sut.ExecuteJobAsync(job);

        // Assert
        _storage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded && r.RetryAt == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteJobAsync_ExpiredJob_MarksExpiredAndSkips()
    {
        // Arrange
        var job = MakeJob<TestJob, TestInput>(new TestInput("test"), expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        // Act
        await _sut.ExecuteJobAsync(job);

        // Assert
        _storage.Verify(x => x.SetExpiredAsync(job.Id, It.IsAny<CancellationToken>()), Times.Once);
        _serviceProvider.Verify(x => x.GetService(typeof(TestJob)), Times.Never);
    }

    [Fact]
    public async Task ExecuteJobAsync_JobWithDeadLetterHandler_InvokesHandler()
    {
        // Arrange
        var job = MakeJob<FailingJob, TestInput>(new TestInput("test"), maxAttempts: 1);
        job.Attempts = 1;
        var jobInstance = new FailingJob();
        var handler = new Mock<IDeadLetterHandler<FailingJob>>();
        _serviceProvider.Setup(x => x.GetService(typeof(FailingJob))).Returns(jobInstance);
        _serviceProvider.Setup(x => x.GetService(typeof(IDeadLetterHandler<FailingJob>))).Returns(handler.Object);
        _migrationPipeline.Setup(x => x.Migrate(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Type>()))
            .Returns(job.InputJson);

        // Act
        await _sut.ExecuteJobAsync(job);

        // Assert
        handler.Verify(x => x.HandleAsync(job, It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteJobAsync_DeadLetterHandlerThrows_DoesNotCrash()
    {
        // Arrange
        var job = MakeJob<FailingJob, TestInput>(new TestInput("test"), maxAttempts: 1);
        job.Attempts = 1;
        var jobInstance = new FailingJob();
        var handler = new Mock<IDeadLetterHandler<FailingJob>>();
        handler.Setup(x => x.HandleAsync(It.IsAny<JobRecord>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("handler failure"));
        _serviceProvider.Setup(x => x.GetService(typeof(FailingJob))).Returns(jobInstance);
        _serviceProvider.Setup(x => x.GetService(typeof(IDeadLetterHandler<FailingJob>))).Returns(handler.Object);
        _migrationPipeline.Setup(x => x.Migrate(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Type>()))
            .Returns(job.InputJson);

        // Act
        Func<Task> act = () => _sut.ExecuteJobAsync(job);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteJobAsync_WithFilters_FiltersWrapExecution()
    {
        // Arrange
        var job = MakeJob<TestJob, TestInput>(new TestInput("test"));
        var jobInstance = new TestJob();
        var filter = new Mock<IJobExecutionFilter>();
        filter.Setup(x => x.OnExecutingAsync(It.IsAny<JobExecutingContext>(), It.IsAny<JobExecutionDelegate>(), It.IsAny<CancellationToken>()))
            .Returns<JobExecutingContext, JobExecutionDelegate, CancellationToken>((ctx, next, cancellationToken) => next(cancellationToken));

        _serviceProvider.Setup(x => x.GetService(typeof(TestJob))).Returns(jobInstance);
        _migrationPipeline.Setup(x => x.Migrate(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Type>()))
            .Returns(job.InputJson);

        var sutWithFilters = new JobExecutor(
            _storage.Object,
            _scopeFactory.Object,
            _throttleRegistry,
            _options,
            new[] { filter.Object },
            NullLogger<JobExecutor>.Instance);

        // Act
        await sutWithFilters.ExecuteJobAsync(job);

        // Assert
        filter.Verify(x => x.OnExecutingAsync(It.IsAny<JobExecutingContext>(), It.IsAny<JobExecutionDelegate>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteJobAsync_FilterThrows_TreatedAsJobFailure()
    {
        // Arrange
        var job = MakeJob<TestJob, TestInput>(new TestInput("test"), maxAttempts: 1);
        job.Attempts = 1;
        var jobInstance = new TestJob();
        var filter = new Mock<IJobExecutionFilter>();
        filter.Setup(x => x.OnExecutingAsync(It.IsAny<JobExecutingContext>(), It.IsAny<JobExecutionDelegate>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("filter failure"));

        _serviceProvider.Setup(x => x.GetService(typeof(TestJob))).Returns(jobInstance);
        _migrationPipeline.Setup(x => x.Migrate(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<Type>()))
            .Returns(job.InputJson);

        var sutWithFilters = new JobExecutor(
            _storage.Object,
            _scopeFactory.Object,
            _throttleRegistry,
            _options,
            new[] { filter.Object },
            NullLogger<JobExecutor>.Instance);

        // Act
        await sutWithFilters.ExecuteJobAsync(job);

        // Assert
        _storage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static JobRecord MakeJob<TJob, TInput>(TInput input, int maxAttempts = 5, DateTimeOffset? expiresAt = null)
    {
        return new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(TJob).AssemblyQualifiedName!,
            InputType = typeof(TInput).AssemblyQualifiedName!,
            InputJson = JsonSerializer.Serialize(input),
            Queue = "default",
            Attempts = 1,
            MaxAttempts = maxAttempts,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = JobStatus.Enqueued,
            ExpiresAt = expiresAt,
        };
    }

    public sealed class TestInput
    {
        public string Value { get; }
        public TestInput(string value) => Value = value;
    }

    public sealed class TestJob : IJob<TestInput>
    {
        public Task ExecuteAsync(TestInput input, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class FailingJob : IJob<TestInput>
    {
        public Task ExecuteAsync(TestInput input, CancellationToken cancellationToken) => throw new Exception("job failed");
    }
}
