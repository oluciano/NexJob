using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Tests for issue #256: storage errors in the heartbeat and in the success commit must not
/// fail a job that ran successfully, and must not end the heartbeat loop.
/// </summary>
public sealed class JobExecutorStorageErrorTests
{
    private static readonly TimeSpan[] FastRetries = [TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1)];

    private readonly Mock<IJobStorage> _storage = new();
    private readonly Mock<IJobInvokerFactory> _invoker = new();
    private readonly Mock<IJobRetryPolicy> _retry = new();
    private readonly Mock<IDeadLetterDispatcher> _deadLetter = new();

    // ─── N1: heartbeat survives a storage error ──────────────────────────────

    /// <summary>N1 (Positive): a heartbeat failure does not stop later heartbeats.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task HeartbeatFailsOnce_LaterHeartbeatsStillSent()
    {
        var calls = 0;
        _storage.Setup(x => x.UpdateHeartbeatAsync(It.IsAny<JobId>(), It.IsAny<CancellationToken>()))
            .Returns(() => Interlocked.Increment(ref calls) == 1
                ? Task.FromException(new InvalidOperationException("db blip"))
                : Task.CompletedTask);
        var job = NewJob();
        await using var sut = Build(TimeSpan.FromMilliseconds(10));
        SetupInvoker(job, async ct => await Task.Delay(300, ct));

        await sut.ExecuteJobAsync(job);

        calls.Should().BeGreaterThan(2, "the loop must keep sending heartbeats after the first one failed");
        Committed(job, r => r.Succeeded).Should().Be(1);
    }

    /// <summary>N3 (Boundary): when storage always throws, the heartbeat is bounded by the interval and does not spin.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task StorageAlwaysThrows_HeartbeatDoesNotSpin()
    {
        var calls = 0;
        _storage.Setup(x => x.UpdateHeartbeatAsync(It.IsAny<JobId>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromException(new InvalidOperationException("down"));
            });
        var job = NewJob();
        await using var sut = Build(TimeSpan.FromMilliseconds(50));
        SetupInvoker(job, async ct => await Task.Delay(400, ct));

        await sut.ExecuteJobAsync(job);

        calls.Should().BeInRange(1, 12, "one attempt per interval at most");
        Committed(job, r => r.Succeeded).Should().Be(1);
    }

    /// <summary>N3 (Invalid input): a cancelled heartbeat stops cleanly and a fast job never sends one.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task HeartbeatCancelled_StopsCleanly()
    {
        var job = NewJob();
        await using var sut = Build(TimeSpan.FromMinutes(5));
        SetupInvoker(job, _ => Task.CompletedTask);

        var act = () => sut.ExecuteJobAsync(job);

        await act.Should().NotThrowAsync();
        _storage.Verify(x => x.UpdateHeartbeatAsync(It.IsAny<JobId>(), It.IsAny<CancellationToken>()), Times.Never);
        Committed(job, r => r.Succeeded).Should().Be(1);
    }

    // ─── N2: success commit failures ─────────────────────────────────────────

    /// <summary>N2 (Negative): a success commit that always fails on the last attempt is neither failed nor dead-lettered.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task SuccessCommitFails_OnLastAttempt_NoDeadLetter_NotMarkedFailed()
    {
        _storage.Setup(x => x.CommitJobResultAsync(It.IsAny<JobId>(), It.IsAny<JobExecutionResult>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("commit down"));
        var job = NewJob(attempts: 3, maxAttempts: 3);
        await using var sut = Build(TimeSpan.FromMinutes(5));
        SetupInvoker(job, _ => Task.CompletedTask);

        var act = () => sut.ExecuteJobAsync(job);

        await act.Should().NotThrowAsync("the dispatcher must not see the storage error");
        Committed(job, r => !r.Succeeded).Should().Be(0, "a successful job must never be committed as failed");
        Committed(job, r => r.Succeeded).Should().Be(4, "1 attempt + 3 retries");
        _deadLetter.Verify(x => x.DispatchAsync(It.IsAny<JobRecord>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
        _retry.Verify(x => x.ComputeRetryAt(It.IsAny<JobRecord>(), It.IsAny<Exception>()), Times.Never);
    }

    /// <summary>N1 (Positive): a success commit that fails once is retried and the job ends Succeeded.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task SuccessCommitFailsOnceThenSucceeds_JobSucceeded()
    {
        var calls = 0;
        _storage.Setup(x => x.CommitJobResultAsync(It.IsAny<JobId>(), It.IsAny<JobExecutionResult>(), It.IsAny<CancellationToken>()))
            .Returns(() => Interlocked.Increment(ref calls) == 1
                ? Task.FromException(new InvalidOperationException("blip"))
                : Task.CompletedTask);
        var job = NewJob();
        await using var sut = Build(TimeSpan.FromMinutes(5));
        SetupInvoker(job, _ => Task.CompletedTask);

        await sut.ExecuteJobAsync(job);

        calls.Should().Be(2);
        Committed(job, r => !r.Succeeded).Should().Be(0);
        _deadLetter.Verify(x => x.DispatchAsync(It.IsAny<JobRecord>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>N3 (Invalid input): a job failure still goes through the normal failure path, untouched by the commit retry logic.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task JobFailure_StillUsesRetryPolicyAndDeadLetter()
    {
        var job = NewJob(attempts: 1, maxAttempts: 1);
        await using var sut = Build(TimeSpan.FromMinutes(5));
        SetupInvoker(job, _ => throw new InvalidOperationException("job bug"));
        _retry.Setup(x => x.ComputeRetryAt(job, It.IsAny<Exception>())).Returns((DateTimeOffset?)null);

        await sut.ExecuteJobAsync(job);

        Committed(job, r => !r.Succeeded && r.RetryAt == null).Should().Be(1);
        _deadLetter.Verify(x => x.DispatchAsync(job, It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static JobRecord NewJob(int attempts = 1, int maxAttempts = 3) =>
        new() { Id = JobId.New(), JobType = "TestJob", Attempts = attempts, MaxAttempts = maxAttempts };

    private JobExecutor Build(TimeSpan heartbeatInterval) =>
        new(
            _storage.Object,
            _invoker.Object,
            _retry.Object,
            _deadLetter.Object,
            new ThrottleRegistry(),
            new NexJobOptions { HeartbeatInterval = heartbeatInterval },
            Enumerable.Empty<IJobExecutionFilter>(),
            NullLogger<JobExecutor>.Instance)
        {
            CommitRetryDelays = FastRetries,
        };

    private void SetupInvoker(JobRecord job, Func<CancellationToken, Task> body)
    {
        var context = new JobInvocationContext(
            new Mock<IServiceScope>().Object,
            new object(),
            new object(),
            (object _, object _, CancellationToken ct) => body(ct),
            Array.Empty<ThrottleAttribute>());
        _invoker.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>())).ReturnsAsync(context);
    }

    private int Committed(JobRecord job, Func<JobExecutionResult, bool> predicate) =>
        _storage.Invocations
            .Count(i => i.Method.Name == nameof(IJobStorage.CommitJobResultAsync)
                        && (JobId)i.Arguments[0] == job.Id
                        && predicate((JobExecutionResult)i.Arguments[1]));
}
