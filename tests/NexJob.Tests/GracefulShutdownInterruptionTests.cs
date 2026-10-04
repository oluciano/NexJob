using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob.Internal;
using NexJob.Storage;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Tests for issue #259: the dispatcher stops fetching as soon as shutdown begins, and
/// jobs interrupted by shutdown are requeued without consuming an attempt or being dead-lettered.
/// </summary>
public sealed class GracefulShutdownInterruptionTests
{
    // ─── N1: dispatcher stops fetching once StopAsync begins ─────────────────

    /// <summary>N1 (Positive): a job enqueued after shutdown began is never fetched, even with idle worker slots.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task StopAsync_NoNewFetchAfterStopBegins()
    {
        var jobStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseJob = new SemaphoreSlim(0, 1);

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(opt =>
                {
                    opt.Workers = 2; // one slot stays idle while the gated job drains
                    opt.MaxAttempts = 1;
                    opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                    opt.ShutdownTimeout = TimeSpan.FromSeconds(10);
                });
                services.AddTransient(_ => new GateableJob(jobStarted, releaseJob));
            })
            .Build();

        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<GateableJob, GateableInput>(new());
        await jobStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stopTask = host.StopAsync();
        await Task.Delay(200); // shutdown is now draining the gated job

        await scheduler.EnqueueAsync<GateableJob, GateableInput>(new());
        await Task.Delay(300);

        var storage = (InMemoryStorageProvider)host.Services.GetRequiredService<IStorageProvider>();
        (await storage.GetMetricsAsync()).Processing.Should().Be(1, "only the in-flight job may run; nothing new is fetched during drain");

        releaseJob.Release();
        await stopTask;

        (await storage.GetMetricsAsync()).Succeeded.Should().Be(1, "the job enqueued during shutdown must stay untouched");
    }

    // ─── executor: interruption handling ─────────────────────────────────────

    /// <summary>N2 (Negative): a job on its last attempt cancelled by shutdown is requeued, keeps its attempt and is not dead-lettered.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task JobCancelledByShutdown_AttemptNotConsumed_NoDeadLetter()
    {
        var (sut, storage, invoker, retry, deadLetter) = BuildExecutor();
        var job = new JobRecord { Id = JobId.New(), JobType = "TestJob", Attempts = 3, MaxAttempts = 3 };
        using var shutdown = new CancellationTokenSource();
        invoker.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .Callback(() => shutdown.Cancel())
            .ThrowsAsync(new OperationCanceledException(shutdown.Token));

        await sut.ExecuteJobAsync(job, shutdown.Token);

        // Behavior changed in v5.8: the attempt is given back by the storage (RefundAttempt, issue #327). Editing the local
        // JobRecord was never persisted by the database providers, so the executor no longer touches it.
        job.Attempts.Should().Be(3, "the executor leaves the local copy alone; the storage refunds the attempt");
        storage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded && r.RetryAt != null && r.RetryAt <= DateTimeOffset.UtcNow.AddSeconds(1) && r.RefundAttempt),
            It.IsAny<CancellationToken>()), Times.Once);
        deadLetter.Verify(x => x.DispatchAsync(It.IsAny<JobRecord>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
        retry.Verify(x => x.ComputeRetryAt(It.IsAny<JobRecord>(), It.IsAny<Exception>()), Times.Never);
    }

    /// <summary>N3 (Invalid input): OperationCanceledException without shutdown is an ordinary failure and goes through the retry policy.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task JobThrowsOperationCanceledWithoutShutdown_IsNormalFailure()
    {
        var (sut, storage, invoker, retry, deadLetter) = BuildExecutor();
        var job = new JobRecord { Id = JobId.New(), JobType = "TestJob", Attempts = 1, MaxAttempts = 1 };
        var oce = new OperationCanceledException("job-internal timeout");
        invoker.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>())).ThrowsAsync(oce);
        retry.Setup(x => x.ComputeRetryAt(job, oce)).Returns((DateTimeOffset?)null);

        await sut.ExecuteJobAsync(job, CancellationToken.None);

        job.Attempts.Should().Be(1);
        retry.Verify(x => x.ComputeRetryAt(job, oce), Times.Once);
        deadLetter.Verify(x => x.DispatchAsync(job, oce, It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded && r.RetryAt == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>N3 (Boundary): an interrupted job with zero attempts never yields a negative attempt count.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task JobCancelledByShutdown_WithZeroAttempts_DoesNotGoNegative()
    {
        var (sut, storage, invoker, _, _) = BuildExecutor();
        var job = new JobRecord { Id = JobId.New(), JobType = "TestJob", Attempts = 0, MaxAttempts = 3 };
        using var shutdown = new CancellationTokenSource();
        invoker.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .Callback(() => shutdown.Cancel())
            .ThrowsAsync(new OperationCanceledException(shutdown.Token));

        await sut.ExecuteJobAsync(job, shutdown.Token);

        job.Attempts.Should().Be(0);
        storage.Verify(x => x.CommitJobResultAsync(
            job.Id,
            It.Is<JobExecutionResult>(r => !r.Succeeded && r.RetryAt != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (
        JobExecutor Sut,
        Mock<IJobStorage> Storage,
        Mock<IJobInvokerFactory> Invoker,
        Mock<IJobRetryPolicy> Retry,
        Mock<IDeadLetterDispatcher> DeadLetter) BuildExecutor()
    {
        var storage = new Mock<IJobStorage>();
        var invoker = new Mock<IJobInvokerFactory>();
        var retry = new Mock<IJobRetryPolicy>();
        var deadLetter = new Mock<IDeadLetterDispatcher>();
        var sut = new JobExecutor(
            storage.Object,
            invoker.Object,
            retry.Object,
            deadLetter.Object,
            new ThrottleRegistry(),
            new NexJobOptions { HeartbeatInterval = TimeSpan.FromMilliseconds(10) },
            Enumerable.Empty<IJobExecutionFilter>(),
            NullLogger<JobExecutor>.Instance);
        return (sut, storage, invoker, retry, deadLetter);
    }
}
