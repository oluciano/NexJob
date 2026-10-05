using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Execution timeout (#354): the limit cancels the job token and flows through the normal failure path.
/// </summary>
public sealed class ExecutionTimeoutTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(150);

    private readonly Mock<IJobStorage> _storage = new();
    private readonly Mock<IJobInvokerFactory> _invokerFactory = new();
    private readonly Mock<IJobRetryPolicy> _retryPolicy = new();
    private readonly Mock<IDeadLetterDispatcher> _deadLetterDispatcher = new();
    private readonly ThrottleRegistry _throttleRegistry = new();
    private readonly NexJobOptions _options = new() { HeartbeatInterval = TimeSpan.FromMilliseconds(10) };

    private JobExecutor Sut => new(
        _storage.Object,
        _invokerFactory.Object,
        _retryPolicy.Object,
        _deadLetterDispatcher.Object,
        _throttleRegistry,
        _options,
        Enumerable.Empty<IJobExecutionFilter>(),
        NullLogger<JobExecutor>.Instance);

    // ─── N1: positive ──────────────────────────────────────────────────────

    /// <summary>N1: a job that honours the token is cancelled at the limit and recorded as a timeout failure that is retried.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenAttributeTimeoutElapses_RecordsTimeoutAndRetries()
    {
        var job = NewJob();
        var retryAt = DateTimeOffset.UtcNow.AddMinutes(1);
        _retryPolicy.Setup(x => x.ComputeRetryAt(job, It.IsAny<Exception>())).Returns(retryAt);
        SetupInvoker(job, (_, ct) => Task.Delay(TimeSpan.FromSeconds(3), ct), new ExecutionTimeoutAttribute("00:00:00.150"));

        await Sut.ExecuteJobAsync(job).WaitAsync(TimeSpan.FromSeconds(10));

        _storage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded
                && r.RetryAt == retryAt
                && !r.RefundAttempt
                && r.Exception is TimeoutException
                && r.Exception.Message.Contains("timed out after", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
        _deadLetterDispatcher.Verify(x => x.DispatchAsync(It.IsAny<JobRecord>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>N1: on the last attempt a timeout dead-letters the job like any other failure.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenTimeoutOnLastAttempt_DispatchesToDeadLetter()
    {
        var job = NewJob();
        _retryPolicy.Setup(x => x.ComputeRetryAt(job, It.IsAny<Exception>())).Returns((DateTimeOffset?)null);
        SetupInvoker(job, (_, ct) => Task.Delay(TimeSpan.FromSeconds(3), ct), new ExecutionTimeoutAttribute("00:00:00.150"));

        await Sut.ExecuteJobAsync(job).WaitAsync(TimeSpan.FromSeconds(10));

        _deadLetterDispatcher.Verify(x => x.DispatchAsync(job, It.Is<Exception>(e => e is TimeoutException), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>N1: the global default applies when the job type has no attribute.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenOnlyDefaultTimeoutConfigured_AppliesIt()
    {
        _options.DefaultExecutionTimeout = Short;
        var job = NewJob();
        _retryPolicy.Setup(x => x.ComputeRetryAt(job, It.IsAny<Exception>())).Returns(DateTimeOffset.UtcNow.AddMinutes(1));
        SetupInvoker(job, (_, ct) => Task.Delay(TimeSpan.FromSeconds(3), ct), attribute: null);

        await Sut.ExecuteJobAsync(job).WaitAsync(TimeSpan.FromSeconds(10));

        _storage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded && r.Exception is TimeoutException),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>N1: the attribute wins over the global default (a generous attribute lets a slow job finish).</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenAttributeAndDefaultBothSet_AttributeWins()
    {
        _options.DefaultExecutionTimeout = TimeSpan.FromMilliseconds(50);
        var job = NewJob();
        SetupInvoker(job, (_, ct) => Task.Delay(TimeSpan.FromMilliseconds(300), ct), new ExecutionTimeoutAttribute("00:00:30"));

        await Sut.ExecuteJobAsync(job).WaitAsync(TimeSpan.FromSeconds(10));

        _storage.Verify(x => x.CommitJobResultAsync(job.Id, It.Is<JobExecutionResult>(r => r.Succeeded), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>N1: a filter that wraps the job sees the TimeoutException, not a bare cancellation.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenTimeoutElapses_FilterSeesTimeoutException()
    {
        var job = NewJob();
        var filter = new CapturingFilter();
        var sut = new JobExecutor(
            _storage.Object,
            _invokerFactory.Object,
            _retryPolicy.Object,
            _deadLetterDispatcher.Object,
            _throttleRegistry,
            _options,
            new IJobExecutionFilter[] { filter },
            NullLogger<JobExecutor>.Instance);
        _retryPolicy.Setup(x => x.ComputeRetryAt(job, It.IsAny<Exception>())).Returns(DateTimeOffset.UtcNow.AddMinutes(1));
        SetupInvoker(job, (_, ct) => Task.Delay(TimeSpan.FromSeconds(3), ct), new ExecutionTimeoutAttribute("00:00:00.150"));

        await sut.ExecuteJobAsync(job).WaitAsync(TimeSpan.FromSeconds(10));

        filter.Seen.Should().BeOfType<TimeoutException>();
    }

    // ─── N2: negative ──────────────────────────────────────────────────────

    /// <summary>N2: shutdown is not a timeout. The attempt is refunded and nothing is dead-lettered, even with a timeout set.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenShutdownBeforeTimeout_RefundsAttemptAndIsNotATimeout()
    {
        var job = NewJob();
        using var shutdown = new CancellationTokenSource();
        SetupInvoker(
            job,
            async (_, ct) =>
            {
                await shutdown.CancelAsync();
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            },
            new ExecutionTimeoutAttribute("00:00:30"));

        await Sut.ExecuteJobAsync(job, shutdown.Token).WaitAsync(TimeSpan.FromSeconds(10));

        _storage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded && r.RefundAttempt && !(r.Exception is TimeoutException)),
            It.IsAny<CancellationToken>()), Times.Once);
        _deadLetterDispatcher.Verify(x => x.DispatchAsync(It.IsAny<JobRecord>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>N2 (guard): waiting for a throttled resource is not counted towards the limit; it still defers and refunds.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenThrottleWaitExceedsTimeout_StaysAThrottleDeferralNotATimeout()
    {
        _options.ThrottleMaxWait = TimeSpan.FromMilliseconds(600);
        (await _throttleRegistry.TryAcquireWithWaitAsync("busy", 1, TimeSpan.FromSeconds(1), CancellationToken.None)).Should().BeTrue();
        var job = NewJob();
        SetupInvoker(
            job,
            (_, _) => Task.CompletedTask,
            new ExecutionTimeoutAttribute("00:00:00.150"),
            new ThrottleAttribute("busy", 1));

        await Sut.ExecuteJobAsync(job).WaitAsync(TimeSpan.FromSeconds(10));

        _storage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded && r.RefundAttempt && r.Exception is ThrottleDeferredException),
            It.IsAny<CancellationToken>()), Times.Once);
        _retryPolicy.Verify(x => x.ComputeRetryAt(It.IsAny<JobRecord>(), It.IsAny<Exception>()), Times.Never);
    }

    /// <summary>N2 (guard): a job that finishes inside the limit succeeds and is not touched.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenJobFinishesWithinTimeout_Succeeds()
    {
        var job = NewJob();
        SetupInvoker(job, (_, _) => Task.CompletedTask, new ExecutionTimeoutAttribute("00:00:30"));

        await Sut.ExecuteJobAsync(job);

        _storage.Verify(x => x.CommitJobResultAsync(job.Id, It.Is<JobExecutionResult>(r => r.Succeeded), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── N3: invalid input ─────────────────────────────────────────────────

    /// <summary>N3 (guard): with no timeout configured a slow job is never cut short.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenNoTimeoutConfigured_NeverTimesOut()
    {
        _options.DefaultExecutionTimeout.Should().BeNull();
        var job = NewJob();
        SetupInvoker(job, (_, ct) => Task.Delay(TimeSpan.FromMilliseconds(300), ct), attribute: null);

        await Sut.ExecuteJobAsync(job).WaitAsync(TimeSpan.FromSeconds(10));

        _storage.Verify(x => x.CommitJobResultAsync(job.Id, It.Is<JobExecutionResult>(r => r.Succeeded), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>N3: the attribute rejects null, blank, malformed, zero and negative values.</summary>
    /// <param name="value">The raw attribute argument.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-timespan")]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    public void ExecutionTimeoutAttribute_WithInvalidValue_Throws(string? value)
    {
        var act = () => new ExecutionTimeoutAttribute(value!);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>N3: a valid value is parsed.</summary>
    [Fact]
    public void ExecutionTimeoutAttribute_WithValidValue_ParsesIt()
    {
        new ExecutionTimeoutAttribute("00:05:00").Timeout.Should().Be(TimeSpan.FromMinutes(5));
    }

    /// <summary>N3: the global default rejects zero and negative values and accepts null.</summary>
    [Fact]
    public void DefaultExecutionTimeout_RejectsNonPositiveAndAcceptsNull()
    {
        var options = new NexJobOptions();

        options.DefaultExecutionTimeout.Should().BeNull();
        ((Action)(() => options.DefaultExecutionTimeout = TimeSpan.Zero)).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => options.DefaultExecutionTimeout = TimeSpan.FromSeconds(-1))).Should().Throw<ArgumentOutOfRangeException>();

        options.DefaultExecutionTimeout = TimeSpan.FromMinutes(1);
        options.DefaultExecutionTimeout = null;
        options.DefaultExecutionTimeout.Should().BeNull();
    }

    private sealed class CapturingFilter : IJobExecutionFilter
    {
        public Exception? Seen { get; private set; }

        public async Task OnExecutingAsync(JobExecutingContext context, JobExecutionDelegate next, CancellationToken ct)
        {
            try
            {
                await next(ct);
            }
            catch (Exception ex)
            {
                Seen = ex;
                throw;
            }
        }
    }

    private static JobRecord NewJob() => new() { Id = JobId.New(), JobType = "TestJob", Attempts = 1, MaxAttempts = 3 };

    private void SetupInvoker(
        JobRecord job,
        Func<object, CancellationToken, Task> body,
        ExecutionTimeoutAttribute? attribute,
        params ThrottleAttribute[] throttles)
    {
        _invokerFactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new JobInvocationContext(
                new Mock<IServiceScope>().Object,
                new object(),
                new object(),
                (j, _, ct) => body(j, ct),
                throttles,
                ExecutionTimeoutAttribute: attribute));
    }
}
