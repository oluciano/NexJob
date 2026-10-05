using System.Diagnostics.Metrics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NexJob.Storage;
using NexJob.Tests;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// A job that ignores its token after the execution timeout (#367) is made visible: one warning and one counter
/// increment per run. The outcome of the job never changes.
/// </summary>
public sealed class CancellationIgnoredWarningTests
{
    private const string CounterName = "nexjob.jobs.cancellation_ignored";

    private readonly Mock<IJobStorage> _storage = new();
    private readonly Mock<IJobInvokerFactory> _invokerFactory = new();
    private readonly Mock<IJobRetryPolicy> _retryPolicy = new();
    private readonly Mock<IDeadLetterDispatcher> _deadLetterDispatcher = new();
    private readonly LevelLogSink _sink = new();
    private readonly NexJobOptions _options = new()
    {
        HeartbeatInterval = TimeSpan.FromMilliseconds(10),
        CancellationGracePeriod = TimeSpan.FromMilliseconds(200),
    };

    // ─── N1: the stuck job is made visible ──────────────────────────────────

    /// <summary>N1: a job that ignores the token past the grace period is logged once and counted once.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenJobIgnoresCancellationPastGrace_WarnsAndCountsOnce()
    {
        var job = NewJob();
        using var counter = new CounterCapture(job.JobType);
        SetupInvoker(job, (_, _) => Task.Delay(TimeSpan.FromMilliseconds(1500)), "00:00:00.100");

        await NewSut().ExecuteJobAsync(job).WaitAsync(TimeSpan.FromSeconds(10));

        Warnings().Should().ContainSingle("it is logged once per run, never repeated")
            .Which.Should().Contain(job.Id.Value.ToString()).And.Contain("ignored cancellation");
        counter.Total.Should().Be(1);
    }

    /// <summary>N1: the outcome is unchanged. A job that ignores the token and later returns normally still succeeds.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenJobIgnoresCancellationButReturns_StillSucceeds()
    {
        var job = NewJob();
        SetupInvoker(job, (_, _) => Task.Delay(TimeSpan.FromMilliseconds(800)), "00:00:00.100");

        await NewSut().ExecuteJobAsync(job).WaitAsync(TimeSpan.FromSeconds(10));

        _storage.Verify(x => x.CommitJobResultAsync(job.Id, It.Is<JobExecutionResult>(r => r.Succeeded), It.IsAny<CancellationToken>()), Times.Once);
        _deadLetterDispatcher.Verify(x => x.DispatchAsync(It.IsAny<JobRecord>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── N2: nothing to warn about ──────────────────────────────────────────

    /// <summary>N2: a job that honours the token returns inside the grace period, so nothing is logged or counted.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenJobHonoursCancellation_DoesNotWarn()
    {
        var job = NewJob();
        using var counter = new CounterCapture(job.JobType);
        _retryPolicy.Setup(x => x.ComputeRetryAt(job, It.IsAny<Exception>())).Returns(DateTimeOffset.UtcNow.AddMinutes(1));
        SetupInvoker(job, (_, ct) => Task.Delay(TimeSpan.FromSeconds(5), ct), "00:00:00.100");

        await NewSut().ExecuteJobAsync(job).WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Warnings().Should().BeEmpty();
        counter.Total.Should().Be(0);
    }

    /// <summary>N2: a shutdown during the grace period is not a timeout problem, so nothing is logged or counted.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenShutdownDuringGrace_DoesNotWarn()
    {
        var job = NewJob();
        using var counter = new CounterCapture(job.JobType);
        using var shutdown = new CancellationTokenSource();
        SetupInvoker(
            job,
            async (_, _) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150));
                await shutdown.CancelAsync();
                await Task.Delay(TimeSpan.FromMilliseconds(700));
            },
            "00:00:00.100");

        await NewSut().ExecuteJobAsync(job, shutdown.Token).WaitAsync(TimeSpan.FromSeconds(10));

        Warnings().Should().BeEmpty();
        counter.Total.Should().Be(0);
    }

    // ─── N3: boundaries ─────────────────────────────────────────────────────

    /// <summary>N3: with no timeout configured nothing is armed, however long the job runs.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenNoTimeoutConfigured_NeverWarns()
    {
        var job = NewJob();
        using var counter = new CounterCapture(job.JobType);
        SetupInvoker(job, (_, _) => Task.Delay(TimeSpan.FromMilliseconds(600)), timeout: null);

        await NewSut().ExecuteJobAsync(job).WaitAsync(TimeSpan.FromSeconds(10));

        Warnings().Should().BeEmpty();
        counter.Total.Should().Be(0);
    }

    /// <summary>N3: a job that returns just before the grace period ends is not reported.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ExecuteJobAsync_WhenJobReturnsBeforeGraceEnds_DoesNotWarn()
    {
        _options.CancellationGracePeriod = TimeSpan.FromSeconds(2);
        var job = NewJob();
        using var counter = new CounterCapture(job.JobType);
        SetupInvoker(job, (_, _) => Task.Delay(TimeSpan.FromMilliseconds(400)), "00:00:00.100");

        await NewSut().ExecuteJobAsync(job).WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        Warnings().Should().BeEmpty();
        counter.Total.Should().Be(0);
    }

    private static JobRecord NewJob() =>
        new() { Id = JobId.New(), JobType = $"TestJob-{Guid.NewGuid():N}", Attempts = 1, MaxAttempts = 3 };

    private JobExecutor NewSut()
    {
        var factory = LoggerFactory.Create(b => b.AddProvider(_sink));
        return new JobExecutor(
            _storage.Object,
            _invokerFactory.Object,
            _retryPolicy.Object,
            _deadLetterDispatcher.Object,
            new ThrottleRegistry(),
            _options,
            Enumerable.Empty<IJobExecutionFilter>(),
            factory.CreateLogger<JobExecutor>());
    }

    private List<string> Warnings() =>
        _sink.Entries.Where(e => e.Level == LogLevel.Warning && e.Message.Contains("ignored cancellation", StringComparison.Ordinal))
            .Select(e => e.Message)
            .ToList();

    private void SetupInvoker(JobRecord job, Func<object, CancellationToken, Task> body, string? timeout)
    {
        var attribute = timeout is null ? null : new ExecutionTimeoutAttribute(timeout);
        _invokerFactory.Setup(x => x.PrepareAsync(job, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new JobInvocationContext(
                new Mock<IServiceScope>().Object,
                new object(),
                new object(),
                (j, _, ct) => body(j, ct),
                Array.Empty<ThrottleAttribute>(),
                ExecutionTimeoutAttribute: attribute));
    }

    private sealed class CounterCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private long _total;

        public CounterCapture(string jobType)
        {
            _listener.InstrumentPublished = (instrument, l) =>
            {
                if (string.Equals(instrument.Name, CounterName, StringComparison.Ordinal))
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                foreach (var tag in tags)
                {
                    if (string.Equals(tag.Key, "nexjob.job_type", StringComparison.Ordinal)
                        && string.Equals(tag.Value as string, jobType, StringComparison.Ordinal))
                    {
                        Interlocked.Add(ref _total, value);
                    }
                }
            });
            _listener.Start();
        }

        public long Total => Interlocked.Read(ref _total);

        public void Dispose() => _listener.Dispose();
    }
}
