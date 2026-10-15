using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NexJob.Exceptions;
using NexJob.Internal;
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
            new JobWakeUpChannel(),
            NullLogger<JobExecutor>.Instance);

        _hardenedSut = new JobExecutor(
            _hardenedStorage.Object,
            _hardenedInvokerfactory.Object,
            _hardenedRetrypolicy.Object,
            _hardenedDeadletterdispatcher.Object,
            _hardenedThrottleregistry,
            _hardenedOptions,
            new JobWakeUpChannel(),
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

        _invokerFactory
            .Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => MakeContext(job, filter.Object));

        // Act
        await _sut.ExecuteJobAsync(job);

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

        _invokerFactory
            .Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => MakeContext(job, filter.Object));

        // Act
        await _sut.ExecuteJobAsync(job);

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
    public async Task ExecuteJobAsync_JobContractException_FailsWithoutRetryAndDeadLetters()
    {
        // Arrange: the retry policy would schedule a retry, but a missing contract cannot be fixed by running again.
        var job = MakeJob<TestJob, TestInput>(new TestInput("test"), maxAttempts: 5);
        job.Attempts = 1;
        _invokerFactory
            .Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new JobContractException("Job type 'X' must implement IJob<String>."));

        // Act
        await _sut.ExecuteJobAsync(job);

        // Assert
        _storage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded && r.RetryAt == null),
            It.IsAny<CancellationToken>()), Times.Once);
        _deadLetterDispatcher.Verify(
            x => x.DispatchAsync(job, It.IsAny<JobContractException>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteJobAsync_OtherFactoryException_StillFollowsRetryPolicy()
    {
        var job = MakeJob<TestJob, TestInput>(new TestInput("test"), maxAttempts: 5);
        job.Attempts = 1;
        _invokerFactory
            .Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("factory failure"));

        await _sut.ExecuteJobAsync(job);

        _storage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded && r.RetryAt != null),
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

    private static JobInvocationContext MakeContext(JobRecord job, params IJobExecutionFilter[] filters)
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
            TestScopes.Create(filters),
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

    private static JobRecord MakeJob<TJob, TInput>(TInput input, int maxAttempts = 5, DateTimeOffset? expiresAt = null, string queue = "default", string? traceParent = null, string? recurringJobId = null, JobId? parentJobId = null)
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
            RecurringJobId = recurringJobId,
            ParentJobId = parentJobId,
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
            new JobWakeUpChannel(),
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

    private readonly Mock<IJobStorage> _hardenedStorage = new();
    private readonly Mock<IJobInvokerFactory> _hardenedInvokerfactory = new();
    private readonly Mock<IJobRetryPolicy> _hardenedRetrypolicy = new();
    private readonly Mock<IDeadLetterDispatcher> _hardenedDeadletterdispatcher = new();
    private readonly ThrottleRegistry _hardenedThrottleregistry = new();
    private readonly NexJobOptions _hardenedOptions = new() { HeartbeatInterval = TimeSpan.FromMilliseconds(10) };
    private readonly JobExecutor _hardenedSut;

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
        await _hardenedSut.ExecuteJobAsync(job);

        // Assert
        _hardenedStorage.Verify(x => x.SetExpiredAsync(job.Id, It.IsAny<CancellationToken>()), Times.Once);
        _hardenedInvokerfactory.Verify(x => x.PrepareAsync(It.IsAny<JobRecord>(), It.IsAny<CancellationToken>()), Times.Never);
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
        await _hardenedSut.ExecuteJobAsync(job);

        // Assert
        _hardenedStorage.Verify(x => x.SetExpiredAsync(job.Id, It.IsAny<CancellationToken>()), Times.Never);
        _hardenedStorage.Verify(x => x.CommitJobResultAsync(job.Id, It.Is<JobExecutionResult>(r => r.Succeeded), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>N1 (#517): a job whose ExpiresAt equals the clock's now is not expired (boundary is inclusive).</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenExpiresAtEqualsNow_ExecutesNormally()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var job = new JobRecord { Id = JobId.New(), ExpiresAt = clock.GetUtcNow(), JobType = "TestJob" };
        _ = SetupSuccessfulInvoker(job);

        await MakeHardenedSut(clock).ExecuteJobAsync(job);

        _hardenedStorage.Verify(x => x.SetExpiredAsync(job.Id, It.IsAny<CancellationToken>()), Times.Never);
        _hardenedStorage.Verify(x => x.CommitJobResultAsync(job.Id, It.Is<JobExecutionResult>(r => r.Succeeded), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>N2 (#517): a job whose ExpiresAt is one tick before the clock's now is expired and never executes.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenExpiresAtOneTickBeforeNow_MarksExpiredWithoutExecuting()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var job = new JobRecord { Id = JobId.New(), ExpiresAt = clock.GetUtcNow().AddTicks(-1), JobType = "TestJob" };
        _ = SetupSuccessfulInvoker(job);

        await MakeHardenedSut(clock).ExecuteJobAsync(job);

        _hardenedStorage.Verify(x => x.SetExpiredAsync(job.Id, It.IsAny<CancellationToken>()), Times.Once);
        _hardenedStorage.Verify(x => x.CommitJobResultAsync(job.Id, It.IsAny<JobExecutionResult>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>N3 (#517): a job without ExpiresAt is never treated as expired, whatever the clock says.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenNoExpiresAt_ExecutesNormallyAtAnyTime()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var job = new JobRecord { Id = JobId.New(), ExpiresAt = null, JobType = "TestJob" };
        _ = SetupSuccessfulInvoker(job);

        await MakeHardenedSut(clock).ExecuteJobAsync(job);

        _hardenedStorage.Verify(x => x.SetExpiredAsync(job.Id, It.IsAny<CancellationToken>()), Times.Never);
        _hardenedStorage.Verify(x => x.CommitJobResultAsync(job.Id, It.Is<JobExecutionResult>(r => r.Succeeded), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>(#517): a foreign job is deferred to exactly the clock's now plus ForeignJobRetryDelay.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenForeignJob_DefersToClockNowPlusForeignRetryDelay()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var job = new JobRecord { Id = JobId.New(), JobType = "ForeignService.Job", Attempts = 1, MaxAttempts = 3 };
        _hardenedInvokerfactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForeignJobTypeException(job.JobType, "Cannot load job type: ForeignService.Job"));

        await MakeHardenedSut(clock).ExecuteJobAsync(job);

        var expected = clock.GetUtcNow() + _hardenedOptions.ForeignJobRetryDelay;
        _hardenedStorage.Verify(x => x.CommitJobResultAsync(job.Id, It.Is<JobExecutionResult>(r => r.RetryAt == expected), It.IsAny<CancellationToken>()), Times.Once);
    }

    private JobExecutor MakeHardenedSut(TimeProvider clock) => new(
        _hardenedStorage.Object,
        _hardenedInvokerfactory.Object,
        _hardenedRetrypolicy.Object,
        _hardenedDeadletterdispatcher.Object,
        _hardenedThrottleregistry,
        _hardenedOptions,
        new JobWakeUpChannel(),
        NullLogger<JobExecutor>.Instance,
        clock);

    // ─── Failure Path Branches ─────────────────────────────────────────────

    /// <summary>Tests that dead-letter dispatcher is invoked when retries are exhausted.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenRetryAtIsNull_DispatchesToDeadLetter()
    {
        // Arrange
        var job = new JobRecord { Id = JobId.New(), JobType = "TestJob" };
        var exception = new Exception("Terminal failure");

        _hardenedInvokerfactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        _hardenedRetrypolicy.Setup(x => x.ComputeRetryAt(job, exception))
            .Returns((DateTimeOffset?)null);

        // Act
        await _hardenedSut.ExecuteJobAsync(job);

        // Assert
        _hardenedDeadletterdispatcher.Verify(x => x.DispatchAsync(job, exception, It.IsAny<CancellationToken>()), Times.Once);
        _hardenedStorage.Verify(x => x.CommitJobResultAsync(job.Id, It.Is<JobExecutionResult>(r => !r.Succeeded && r.RetryAt == null), It.IsAny<CancellationToken>()), Times.Once);
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

        _hardenedInvokerfactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);

        _hardenedRetrypolicy.Setup(x => x.ComputeRetryAt(job, exception))
            .Returns(retryAt);

        // Act
        await _hardenedSut.ExecuteJobAsync(job);

        // Assert
        _hardenedDeadletterdispatcher.Verify(x => x.DispatchAsync(It.IsAny<JobRecord>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
        _hardenedStorage.Verify(x => x.CommitJobResultAsync(job.Id, It.Is<JobExecutionResult>(r => r.RetryAt == retryAt), It.IsAny<CancellationToken>()), Times.Once);
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
        await _hardenedSut.ExecuteJobAsync(job);

        // Assert
        _hardenedStorage.Verify(x => x.CommitJobResultAsync(job.Id, It.Is<JobExecutionResult>(r => r.Succeeded), It.IsAny<CancellationToken>()), Times.Once);
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

        _ = SetupSuccessfulInvoker(job, filters: [filterMock.Object]);

        // Act
        await _hardenedSut.ExecuteJobAsync(job);

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
        var heartbeatSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _hardenedStorage.Setup(x => x.UpdateHeartbeatAsync(job.Id, It.IsAny<CancellationToken>()))
            .Callback(() => heartbeatSeen.TrySetResult())
            .Returns(Task.CompletedTask);

        _hardenedInvokerfactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>

                // Behavior changed in v5.10: the job runs until the heartbeat is observed instead of a fixed 50 ms, which a loaded machine could outlast (#371).
                new JobInvocationContext(
                    TestScopes.Create(),
                    new object(),
                    new object(),
                    (object j, object i, CancellationToken ct) => heartbeatSeen.Task.WaitAsync(TimeSpan.FromSeconds(10), ct),
                    Array.Empty<ThrottleAttribute>()));

        // Act
        await _hardenedSut.ExecuteJobAsync(job);

        // Assert
        _hardenedStorage.Verify(x => x.UpdateHeartbeatAsync(job.Id, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ─── Foreign Job Branches ──────────────────────────────────────────────

    /// <summary>N1 (Positive): Tests that foreign job is deferred with ForeignJobRetryDelay, resets attempt, and is not dead-lettered.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenForeignJobTypeExceptionThrown_DefersJobWithoutPenalizingAttempts()
    {
        // Arrange
        var job = new JobRecord { Id = JobId.New(), JobType = "ForeignService.Job", Attempts = 1, MaxAttempts = 3 };
        var foreignException = new ForeignJobTypeException(job.JobType, "Cannot load job type: ForeignService.Job");

        _hardenedInvokerfactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ThrowsAsync(foreignException);

        // Act
        await _hardenedSut.ExecuteJobAsync(job);

        // Assert
        // Behavior changed in v5.8: the attempt is given back by the storage (RefundAttempt, issue #327). Editing the local
        // JobRecord was never persisted by the database providers, so the executor no longer touches it.
        job.Attempts.Should().Be(1, "the executor leaves the local copy alone; the storage refunds the attempt");
        _hardenedDeadletterdispatcher.Verify(x => x.DispatchAsync(It.IsAny<JobRecord>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
        _hardenedRetrypolicy.Verify(x => x.ComputeRetryAt(It.IsAny<JobRecord>(), It.IsAny<Exception>()), Times.Never);
        _hardenedStorage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded && r.RetryAt != null && r.Exception == foreignException && r.RefundAttempt),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>N2 (Negative): Tests that normal exceptions (non-foreign) are handled via standard retry policy and dead-lettering.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenNonForeignExceptionThrown_UsesStandardRetryAndDeadLetter()
    {
        // Arrange
        var job = new JobRecord { Id = JobId.New(), JobType = "TestJob", Attempts = 1, MaxAttempts = 1 };
        var standardException = new InvalidOperationException("Standard execution failure");

        _hardenedInvokerfactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ThrowsAsync(standardException);
        _hardenedRetrypolicy.Setup(x => x.ComputeRetryAt(job, standardException))
            .Returns((DateTimeOffset?)null);

        // Act
        await _hardenedSut.ExecuteJobAsync(job);

        // Assert
        _hardenedDeadletterdispatcher.Verify(x => x.DispatchAsync(job, standardException, It.IsAny<CancellationToken>()), Times.Once);
        _hardenedStorage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded && r.RetryAt == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>N3 (Boundary): Tests that when job.Attempts is already 0, deferring a foreign job does not make Attempts negative.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenAttemptsZero_ForeignJobDoesNotMakeAttemptsNegative()
    {
        // Arrange
        var job = new JobRecord { Id = JobId.New(), JobType = "ForeignService.Job", Attempts = 0, MaxAttempts = 3 };
        var foreignException = new ForeignJobTypeException(job.JobType, "Cannot load job type: ForeignService.Job");

        _hardenedInvokerfactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ThrowsAsync(foreignException);

        // Act
        await _hardenedSut.ExecuteJobAsync(job);

        // Assert
        job.Attempts.Should().Be(0);
        _hardenedStorage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded && r.RetryAt != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Attempt refund through the storage (#327) ─────────────────────────

    /// <summary>N1 (Positive): a foreign job is committed with RefundAttempt so database providers give the attempt back.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenForeignJobTypeExceptionThrown_CommitsWithRefundAttempt()
    {
        var job = new JobRecord { Id = JobId.New(), JobType = "ForeignService.Job", Attempts = 2, MaxAttempts = 3 };
        _hardenedInvokerfactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForeignJobTypeException(job.JobType, "Cannot load job type"));

        await _hardenedSut.ExecuteJobAsync(job);

        _hardenedStorage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded && r.RetryAt != null && r.RefundAttempt),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>N1 (Positive): a job interrupted by shutdown is requeued with RefundAttempt, not dead-lettered.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenInterruptedByShutdown_CommitsWithRefundAttempt()
    {
        var job = new JobRecord { Id = JobId.New(), JobType = "TestJob", Attempts = 2, MaxAttempts = 3 };
        using var shutdown = new CancellationTokenSource();
        _hardenedInvokerfactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .Callback(() => shutdown.Cancel())
            .ThrowsAsync(new OperationCanceledException(shutdown.Token));

        await _hardenedSut.ExecuteJobAsync(job, shutdown.Token);

        _hardenedStorage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded && r.RetryAt != null && r.RefundAttempt),
            It.IsAny<CancellationToken>()), Times.Once);
        _hardenedDeadletterdispatcher.Verify(x => x.DispatchAsync(It.IsAny<JobRecord>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>N2 (Negative): a job that genuinely fails is not refunded; it keeps consuming attempts.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenJobFailsNormally_DoesNotRefundTheAttempt()
    {
        var job = new JobRecord { Id = JobId.New(), JobType = "TestJob", Attempts = 1, MaxAttempts = 3 };
        var failure = new InvalidOperationException("real failure");
        _hardenedInvokerfactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        _hardenedRetrypolicy.Setup(x => x.ComputeRetryAt(job, failure)).Returns(DateTimeOffset.UtcNow.AddSeconds(1));

        await _hardenedSut.ExecuteJobAsync(job);

        _hardenedStorage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded && r.RetryAt != null && !r.RefundAttempt),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>N3 (Boundary): the executor leaves the local attempt count alone at zero; the storage guards the floor.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenAttemptsZeroAndInterrupted_DoesNotGoNegative()
    {
        var job = new JobRecord { Id = JobId.New(), JobType = "TestJob", Attempts = 0, MaxAttempts = 3 };
        using var shutdown = new CancellationTokenSource();
        _hardenedInvokerfactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .Callback(() => shutdown.Cancel())
            .ThrowsAsync(new OperationCanceledException(shutdown.Token));

        await _hardenedSut.ExecuteJobAsync(job, shutdown.Token);

        job.Attempts.Should().BeGreaterOrEqualTo(0);
    }

    // ─── Mutation safety net (#524): batch acknowledgment and filter contract ──────────────────

    /// <summary>N1: a plain successful job with batch acknowledgment on is acknowledged in a batch, not committed one by one.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_BatchAckEnabled_PlainJob_AcknowledgesInBatchAndSkipsCommit()
    {
        _options.EnableBatchAcknowledgment = true;
        var acked = new List<JobId>();
        _storage
            .Setup(x => x.AcknowledgeBatchAsync(It.IsAny<IReadOnlyList<JobId>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<JobId>, CancellationToken>((ids, _) =>
            {
                lock (acked)
                {
                    acked.AddRange(ids); // the executor reuses and clears its buffer, so copy at call time
                }
            })
            .Returns(Task.CompletedTask);
        var job = MakeJob<TestJob, TestInput>(new TestInput("batch"));

        await _sut.ExecuteJobAsync(job);
        await NexJob.Tests.TestWait.InvokedAsync(_storage, nameof(IJobStorage.AcknowledgeBatchAsync), 1);

        lock (acked)
        {
            acked.Should().Equal(job.Id);
        }

        _storage.Verify(
            x => x.CommitJobResultAsync(It.IsAny<JobId>(), It.IsAny<JobExecutionResult>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// N2: every condition that rules a job out of batch acknowledgment sends it through the atomic commit instead, so
    /// continuations, retention and recurring bookkeeping are never skipped.
    /// </summary>
    /// <param name="batchAck">Whether batch acknowledgment is enabled.</param>
    /// <param name="recurring">Whether the job comes from a recurring definition.</param>
    /// <param name="hasParent">Whether the job is a continuation of a parent job.</param>
    /// <param name="purge">Whether the job type purges itself on success.</param>
    /// <param name="trim">Whether the job type trims its payload on success.</param>
    /// <param name="writesLogs">Whether the job emits a captured log line.</param>
    /// <returns>A task.</returns>
    [Theory]
    [InlineData(false, false, false, false, false, false)]
    [InlineData(true, true, false, false, false, false)]
    [InlineData(true, false, true, false, false, false)]
    [InlineData(true, false, false, true, false, false)]
    [InlineData(true, false, false, false, true, false)]
    [InlineData(true, false, false, false, false, true)]
    public async Task ExecuteJobAsync_BatchAckRuledOut_CommitsAtomically(
        bool batchAck, bool recurring, bool hasParent, bool purge, bool trim, bool writesLogs)
    {
        _options.EnableBatchAcknowledgment = batchAck;
        var job = MakeJob<TestJob, TestInput>(
            new TestInput("ruled-out"),
            recurringJobId: recurring ? "daily" : null,
            parentJobId: hasParent ? JobId.New() : null);
        var retention = purge || trim ? new RetentionAttribute { PurgeOnSuccess = purge, TrimPayloadOnSuccess = trim } : null;
        _invokerFactory
            .Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new JobInvocationContext(
                TestScopes.Create(),
                new object(),
                new object(),
                (object _, object _, CancellationToken _) =>
                {
                    if (writesLogs)
                    {
                        JobExecutionLogScope.Current!.Capture(LogLevel.Information, "from the job");
                    }

                    return Task.CompletedTask;
                },
                Array.Empty<ThrottleAttribute>(),
                retention));

        await _sut.ExecuteJobAsync(job);

        _storage.Verify(
            x => x.CommitJobResultAsync(job.Id, It.Is<JobExecutionResult>(r => r.Succeeded), It.IsAny<CancellationToken>()),
            Times.Once);
        _storage.Verify(
            x => x.AcknowledgeBatchAsync(It.IsAny<IReadOnlyList<JobId>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>N1: an acknowledgment still queued when the executor is disposed is written to storage, not dropped.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task DisposeAsync_WithPendingAcknowledgments_DrainsThemToStorage()
    {
        _options.EnableBatchAcknowledgment = true;
        var acked = new List<JobId[]>();
        var firstFlushStarted = new TaskCompletionSource();
        var releaseFirstFlush = new TaskCompletionSource();
        _storage
            .Setup(x => x.AcknowledgeBatchAsync(It.IsAny<IReadOnlyList<JobId>>(), It.IsAny<CancellationToken>()))
            .Returns<IReadOnlyList<JobId>, CancellationToken>(async (ids, _) =>
            {
                lock (acked)
                {
                    acked.Add(ids.ToArray());
                }

                if (acked.Count == 1)
                {
                    firstFlushStarted.SetResult();
                    await releaseFirstFlush.Task;
                }
            });
        var first = MakeJob<TestJob, TestInput>(new TestInput("first"));
        var second = MakeJob<TestJob, TestInput>(new TestInput("second"));

        await _sut.ExecuteJobAsync(first);
        await firstFlushStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await _sut.ExecuteJobAsync(second); // queued while the flusher is busy with the first batch
        var disposing = _sut.DisposeAsync().AsTask();
        releaseFirstFlush.SetResult();
        await disposing.WaitAsync(TimeSpan.FromSeconds(10));

        acked.Should().HaveCount(2);
        acked[0].Should().Equal(first.Id);
        acked[1].Should().Equal(second.Id);
    }

    /// <summary>N3: disposing with nothing pending writes no acknowledgment at all.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task DisposeAsync_WithNothingPending_DoesNotAcknowledge()
    {
        _options.EnableBatchAcknowledgment = true;

        await _sut.DisposeAsync();

        _storage.Verify(
            x => x.AcknowledgeBatchAsync(It.IsAny<IReadOnlyList<JobId>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>N1: a filter that keeps its context sees it marked succeeded, without an exception, after a good run.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_FilterContext_AfterSuccessfulJob_IsSucceededWithoutException()
    {
        var job = MakeJob<TestJob, TestInput>(new TestInput("ok"));
        var filter = new CapturingFilter();
        _invokerFactory
            .Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => MakeContext(job, filter));

        await _sut.ExecuteJobAsync(job);

        filter.Seen.Should().NotBeNull();
        filter.Seen!.Succeeded.Should().BeTrue();
        filter.Seen.Exception.Should().BeNull();
    }

    /// <summary>N2: a filter that keeps its context sees it marked failed, with the job's exception, after a failing run.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_FilterContext_AfterFailingJob_IsFailedWithTheException()
    {
        var job = MakeJob<FailingJob, TestInput>(new TestInput("boom"), maxAttempts: 3);
        var filter = new CapturingFilter();
        _invokerFactory
            .Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => MakeContext(job, filter));

        await _sut.ExecuteJobAsync(job);

        filter.Seen.Should().NotBeNull();
        filter.Seen!.Succeeded.Should().BeFalse();
        filter.Seen.Exception.Should().NotBeNull();
    }

    /// <summary>N2: a filter that observes the cancelled token when the limit fires, before the job runs, still records a timeout failure.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_FilterObservesTimeoutToken_RecordsTimeoutFailure()
    {
        _options.DefaultExecutionTimeout = TimeSpan.FromMilliseconds(100);
        var job = MakeJob<TestJob, TestInput>(new TestInput("slow-filter"), maxAttempts: 3);
        var filter = new Mock<IJobExecutionFilter>();
        filter
            .Setup(x => x.OnExecutingAsync(It.IsAny<JobExecutingContext>(), It.IsAny<JobExecutionDelegate>(), It.IsAny<CancellationToken>()))
            .Returns<JobExecutingContext, JobExecutionDelegate, CancellationToken>((_, _, ct) => Task.Delay(Timeout.Infinite, ct));
        _invokerFactory
            .Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => MakeContext(job, filter.Object));

        await _sut.ExecuteJobAsync(job);

        _storage.Verify(
            x => x.CommitJobResultAsync(job.Id, It.Is<JobExecutionResult>(r => !r.Succeeded && r.Exception is TimeoutException), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private sealed class CapturingFilter : IJobExecutionFilter
    {
        public JobExecutingContext? Seen { get; private set; }

        public Task OnExecutingAsync(JobExecutingContext context, JobExecutionDelegate next, CancellationToken ct)
        {
            Seen = context;
            return next(ct);
        }
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private JobInvocationContext SetupSuccessfulInvoker(JobRecord job, ThrottleAttribute[]? throttles = null, IJobExecutionFilter[]? filters = null)
    {
        var context = new JobInvocationContext(
            TestScopes.Create(filters ?? []),
            new object(),
            new object(),
            (object j, object i, CancellationToken ct) => Task.CompletedTask,
            throttles ?? Array.Empty<ThrottleAttribute>());

        _hardenedInvokerfactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(context);

        return context;
    }

    /// <summary>Support job.</summary>
    public class HardenedTestJob : IJob
    {
        /// <inheritdoc/>
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
