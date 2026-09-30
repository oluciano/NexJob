using System.Reflection;
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
    private readonly Mock<IJobInvokerFactory> _invokerFactory = new();
    private readonly Mock<IJobRetryPolicy> _retryPolicy = new();
    private readonly Mock<IDeadLetterDispatcher> _deadLetterDispatcher = new();
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
            _deadLetterDispatcher.Object,
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
        // Act
        await _sut.ExecuteJobAsync(job);

        // Assert
        _deadLetterDispatcher.Verify(x => x.DispatchAsync(
            job,
            It.IsAny<Exception>(),
            It.IsAny<CancellationToken>()), Times.Once);
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
        // Act
        Func<Task> act = () => _sut.ExecuteJobAsync(job);

        // Assert
        await act.Should().NotThrowAsync();
        _deadLetterDispatcher.Verify(x => x.DispatchAsync(
            job,
            It.IsAny<Exception>(),
            It.IsAny<CancellationToken>()), Times.Once);
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
            _deadLetterDispatcher.Object,
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
            _deadLetterDispatcher.Object,
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

    [Fact]
    public async Task ExecuteJobAsync_WithHostCancellationToken_CancelsExecutingJob()
    {
        // Arrange
        var job = MakeJob<CancellableTestJob, TestInput>(new TestInput("test"));
        using var cts = new CancellationTokenSource();

        _invokerFactory
            .Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobRecord j, CancellationToken _) =>
            {
                cts.Cancel();
                return MakeContext(j);
            });

        // Act
        await _sut.ExecuteJobAsync(job, cts.Token);

        // Assert
        _storage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded && r.Exception is OperationCanceledException),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteJobAsync_WithAlreadyCancelledToken_CommitsFailure()
    {
        // Arrange
        var job = MakeJob<CancellableTestJob, TestInput>(new TestInput("test"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        await _sut.ExecuteJobAsync(job, cts.Token);

        // Assert
        _storage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded && r.Exception is OperationCanceledException),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteJobAsync_WithExplicitCancellationToken_PropagatesLinkedToken()
    {
        // Arrange
        var job = MakeJob<TestJob, TestInput>(new TestInput("test"));
        using var cts = new CancellationTokenSource();
        CancellationToken observedToken = default;

        _invokerFactory
            .Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobRecord j, CancellationToken ct) =>
            {
                observedToken = ct;
                return MakeContext(j);
            });

        // Act
        await _sut.ExecuteJobAsync(job, cts.Token);

        // Assert
        observedToken.CanBeCanceled.Should().BeTrue();
        await cts.CancelAsync();
        observedToken.IsCancellationRequested.Should().BeTrue("linked token must observe cancellation from host token");
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

    private static JobRecord MakeJob<TJob, TInput>(TInput input, int maxAttempts = 5, DateTimeOffset? expiresAt = null, string queue = "default", string? traceParent = null)
    {
        return new JobRecord
        {
            Id = JobId.New(),
            JobType = typeof(TJob).AssemblyQualifiedName!,
            InputType = typeof(TInput).AssemblyQualifiedName!,
            InputJson = JsonSerializer.Serialize(input),
            Queue = queue,
            Attempts = 1,
            MaxAttempts = maxAttempts,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = JobStatus.Enqueued,
            ExpiresAt = expiresAt,
            TraceParent = traceParent,
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

    public sealed class CancellableTestJob : IJob<TestInput>
    {
        public async Task ExecuteAsync(TestInput input, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
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
        public object? GetService(Type serviceType) => null;
    }

    // ── Scope-capturing logger helpers ────────────────────────────────────────
    // Suppress StyleCop warnings for interface implementation methods that are required but unused.
#pragma warning disable S1144 // Unused private member
#pragma warning disable S1172 // Unused parameter
#pragma warning disable S1186 // Empty method must explain why
    private sealed class ScopeCapturingLogger : ILogger<JobExecutor>
    {
        private readonly List<IReadOnlyDictionary<string, object?>> _capturedScopes = [];

        public IReadOnlyList<IReadOnlyDictionary<string, object?>> CapturedScopes => _capturedScopes;

        // Used via ILogger interface by JobExecutor to capture the logging scope.
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                _capturedScopes.Add(pairs.ToDictionary(kv => kv.Key, kv => kv.Value));
            }

            return System.Diagnostics.Activity.Current; // non-null disposable, harmless
        }

        // Required by ILogger, log level is unused but method must exist.
        public bool IsEnabled(LogLevel logLevel) => true;

        // Required by ILogger; body intentionally left empty because we only need scope capture for these tests.
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // No operation – logger captures only scope, not individual log entries.
        }
    }
#pragma warning restore S1186
#pragma warning restore S1172
#pragma warning restore S1144

    private JobExecutor MakeSutWithScopeLogger(ScopeCapturingLogger logger) =>
        new(
            _storage.Object,
            _invokerFactory.Object,
            _retryPolicy.Object,
            _deadLetterDispatcher.Object,
            _throttleRegistry,
            _options,
            Enumerable.Empty<IJobExecutionFilter>(),
            logger);

    // ── 3N: ILogger.BeginScope enrichment ────────────────────────────────────

    [Fact]
    public async Task ExecuteJobAsync_SuccessfulJob_ScopeContainsAllExpectedKeys()
    {
        // Arrange — N1: happy path — scope must carry the 4 structured keys
        var logger = new ScopeCapturingLogger();
        var sut = MakeSutWithScopeLogger(logger);
        var job = MakeJob<TestJob, TestInput>(new TestInput("scope-test"), queue: "payments", traceParent: "00-abc123-01");

        // Act
        await sut.ExecuteJobAsync(job);

        // Assert
        logger.CapturedScopes.Should().NotBeEmpty(because: "BeginScope must be called before execution");
        var scope = logger.CapturedScopes[0];
        scope.Should().ContainKey("NexJob.JobId").WhoseValue.Should().Be(job.Id.Value);
        scope.Should().ContainKey("NexJob.JobType").WhoseValue.Should().Be(job.JobType);
        scope.Should().ContainKey("NexJob.Queue").WhoseValue.Should().Be("payments");
        scope.Should().ContainKey("NexJob.Attempt").WhoseValue.Should().Be(1);
        scope.Should().ContainKey("NexJob.TraceParent").WhoseValue.Should().Be("00-abc123-01");
    }

    [Fact]
    public async Task ExecuteJobAsync_FailingJob_ScopeIsCreatedBeforeFailurePath()
    {
        // Arrange — N2: failure path — scope must be established even when job throws
        var logger = new ScopeCapturingLogger();
        var sut = MakeSutWithScopeLogger(logger);
        var job = MakeJob<FailingJob, TestInput>(new TestInput("fail"), maxAttempts: 3);
        job.Attempts = 1;
        _retryPolicy
            .Setup(x => x.ComputeRetryAt(It.IsAny<JobRecord>(), It.IsAny<Exception>()))
            .Returns(DateTimeOffset.UtcNow.AddMinutes(5));

        // Act — should not throw
        await sut.ExecuteJobAsync(job);

        // Assert — scope must have been opened before the job invocation that threw
        logger.CapturedScopes.Should().NotBeEmpty(because: "BeginScope is called before invoking the job");
        var scope = logger.CapturedScopes[0];
        scope.Should().ContainKey("NexJob.JobId");
        scope.Should().ContainKey("NexJob.Queue");
    }

    [Fact]
    public async Task ExecuteJobAsync_NullTraceParent_ScopeCreatedWithEmptyTraceParent()
    {
        // Arrange — N3: null TraceParent — must not throw, key must be present with empty string
        var logger = new ScopeCapturingLogger();
        var sut = MakeSutWithScopeLogger(logger);
        var job = MakeJob<TestJob, TestInput>(new TestInput("no-trace")); // traceParent defaults to null

        // Act
        await sut.ExecuteJobAsync(job);

        // Assert — scope must contain the key and its value must be string.Empty
        logger.CapturedScopes.Should().NotBeEmpty();
        var scope = logger.CapturedScopes[0];
        scope.Should().ContainKey("NexJob.TraceParent")
            .WhoseValue.Should().Be(string.Empty, because: "null TraceParent must map to empty string, not null");
    }
}
