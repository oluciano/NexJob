using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

public sealed class JobExecutorTests
{
    private readonly Mock<IJobStorage> _storage = new();
    private readonly Mock<IJobInvokerFactory> _invokerFactory = new();
    private readonly Mock<IJobRetryPolicy> _retryPolicy = new();
    private readonly TestServiceScopeFactory _scopeFactory = new();
    private readonly ThrottleRegistry _throttleRegistry = new();
    private readonly NexJobOptions _options = new();
    private readonly JobExecutor _sut;

    public JobExecutorTests()
    {
        _invokerFactory
            .Setup(x => x.PrepareAsync(It.IsAny<JobRecord>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobRecord job, CancellationToken _) => MakeContext(job));
        _retryPolicy
            .Setup(x => x.ComputeRetryAt(It.IsAny<JobRecord>(), It.IsAny<Exception>()))
            .Returns(DateTimeOffset.UtcNow.AddMinutes(1));

        _sut = new JobExecutor(
            _storage.Object,
            _invokerFactory.Object,
            _retryPolicy.Object,
            _scopeFactory,
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
        _retryPolicy
            .Setup(x => x.ComputeRetryAt(It.IsAny<JobRecord>(), It.IsAny<Exception>()))
            .Returns(DateTimeOffset.UtcNow.AddMinutes(5));

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
        _retryPolicy
            .Setup(x => x.ComputeRetryAt(It.IsAny<JobRecord>(), It.IsAny<Exception>()))
            .Returns((DateTimeOffset?)null);

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
        _invokerFactory.Verify(x => x.PrepareAsync(It.IsAny<JobRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteJobAsync_JobWithDeadLetterHandler_InvokesHandler()
    {
        // Arrange
        var job = MakeJob<FailingJob, TestInput>(new TestInput("test"), maxAttempts: 1);
        job.Attempts = 1;
        _retryPolicy
            .Setup(x => x.ComputeRetryAt(It.IsAny<JobRecord>(), It.IsAny<Exception>()))
            .Returns((DateTimeOffset?)null);
        var handler = new Mock<IDeadLetterHandler<FailingJob>>();
        _scopeFactory.SetService<IDeadLetterHandler<FailingJob>>(handler.Object);

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
        _retryPolicy
            .Setup(x => x.ComputeRetryAt(It.IsAny<JobRecord>(), It.IsAny<Exception>()))
            .Returns((DateTimeOffset?)null);
        var handler = new Mock<IDeadLetterHandler<FailingJob>>();
        handler.Setup(x => x.HandleAsync(It.IsAny<JobRecord>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("handler failure"));
        _scopeFactory.SetService<IDeadLetterHandler<FailingJob>>(handler.Object);

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
        var filter = new Mock<IJobExecutionFilter>();
        filter.Setup(x => x.OnExecutingAsync(It.IsAny<JobExecutingContext>(), It.IsAny<JobExecutionDelegate>(), It.IsAny<CancellationToken>()))
            .Returns<JobExecutingContext, JobExecutionDelegate, CancellationToken>((ctx, next, cancellationToken) => next(cancellationToken));

        var sutWithFilters = new JobExecutor(
            _storage.Object,
            _invokerFactory.Object,
            _retryPolicy.Object,
            _scopeFactory,
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
        var filter = new Mock<IJobExecutionFilter>();
        filter.Setup(x => x.OnExecutingAsync(It.IsAny<JobExecutingContext>(), It.IsAny<JobExecutionDelegate>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("filter failure"));

        var sutWithFilters = new JobExecutor(
            _storage.Object,
            _invokerFactory.Object,
            _retryPolicy.Object,
            _scopeFactory,
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

    [Fact]
    public async Task ExecuteJobAsync_InvokerFactoryThrows_CommitsFailure()
    {
        // Arrange
        var job = MakeJob<TestJob, TestInput>(new TestInput("test"), maxAttempts: 1);
        job.Attempts = 1;
        _invokerFactory
            .Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("factory failure"));

        // Act
        await _sut.ExecuteJobAsync(job);

        // Assert
        _storage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteJobAsync_InvokerFactory_CalledWithCorrectJob()
    {
        // Arrange
        var job = MakeJob<TestJob, TestInput>(new TestInput("test"));

        // Act
        await _sut.ExecuteJobAsync(job);

        // Assert
        _invokerFactory.Verify(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static JobInvocationContext MakeContext(JobRecord job)
    {
        var jobType = JobTypeResolver.ResolveJobType(job.JobType)
                      ?? throw new InvalidOperationException($"Cannot load job type: {job.JobType}");
        var inputType = JobTypeResolver.ResolveInputType(job.InputType)
                       ?? throw new InvalidOperationException($"Cannot load input type: {job.InputType}");
        var input = JsonSerializer.Deserialize(job.InputJson, inputType)
                    ?? throw new InvalidOperationException($"Deserialized null input for job {job.Id}.");
        var jobInstance = Activator.CreateInstance(jobType)
                          ?? throw new InvalidOperationException($"Cannot create job type: {job.JobType}");
        var method = GetExecuteMethod(jobType, inputType);
        var throttleAttrs = jobType.GetCustomAttributes<ThrottleAttribute>(inherit: true);

        return new JobInvocationContext(
            new TestServiceScope(new TestServiceProvider()),
            jobInstance,
            input,
            (instance, value, cancellationToken) => Invoke(method, inputType, instance, value, cancellationToken),
            throttleAttrs);
    }

    private static Task Invoke(MethodInfo method, Type inputType, object instance, object input, CancellationToken cancellationToken)
    {
        if (inputType == typeof(NoInput))
        {
            return (Task)method.Invoke(instance, new object[] { cancellationToken })!;
        }

        return (Task)method.Invoke(instance, new object[] { input, cancellationToken })!;
    }

    private static MethodInfo GetExecuteMethod(Type jobType, Type inputType)
    {
        if (inputType == typeof(NoInput))
        {
            return jobType.GetMethod(nameof(IJob.ExecuteAsync), new[] { typeof(CancellationToken) })
                   ?? throw new InvalidOperationException($"Cannot find ExecuteAsync method on {jobType.Name}");
        }

        return jobType.GetMethod(nameof(IJob<object>.ExecuteAsync), new[] { inputType, typeof(CancellationToken) })
               ?? throw new InvalidOperationException($"Cannot find ExecuteAsync method on {jobType.Name}");
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

    private sealed class TestServiceScopeFactory : IServiceScopeFactory
    {
        private readonly TestServiceProvider _serviceProvider = new();

        public void SetService<TService>(TService service)
            where TService : class
        {
            _serviceProvider.Set(typeof(TService), service);
        }

        public IServiceScope CreateScope() => new TestServiceScope(_serviceProvider);
    }

    private sealed class TestServiceScope : IServiceScope
    {
        public TestServiceScope(IServiceProvider serviceProvider) => ServiceProvider = serviceProvider;

        public IServiceProvider ServiceProvider { get; }

        public void Dispose()
        {
        }
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        private readonly Dictionary<Type, object> _services = new();

        public object? GetService(Type serviceType)
        {
            _services.TryGetValue(serviceType, out var service);
            return service;
        }

        public void Set(Type serviceType, object service)
        {
            _services[serviceType] = service;
        }
    }
}
