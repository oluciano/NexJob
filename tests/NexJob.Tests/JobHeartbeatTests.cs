using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// The heartbeat loop of a running job: it refreshes storage on an interval, survives a transient storage
/// error, and ends quietly when the job completes.
/// </summary>
public sealed class JobHeartbeatTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(10);

    private readonly Mock<IJobStorage> _storage = new();
    private readonly RecordingLogger _logger = new();
    private readonly NexJobOptions _options = new() { HeartbeatInterval = Tick };

    private JobHeartbeat Sut => new(_storage.Object, _options, _logger);

    // ─── N1: positive ──────────────────────────────────────────────────────

    /// <summary>N1: while the job runs, the heartbeat of that job is refreshed again and again until the token is cancelled.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task RunAsync_WhileRunning_RefreshesTheHeartbeatOfTheJobRepeatedly()
    {
        var jobId = JobId.New();
        using var cts = new CancellationTokenSource();

        var running = Sut.RunAsync(jobId, cts.Token);
        await NexJob.Tests.TestWait.InvokedAsync(_storage, nameof(IJobStorage.UpdateHeartbeatAsync), 3);
        await cts.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        _storage.Verify(x => x.UpdateHeartbeatAsync(jobId, It.IsAny<CancellationToken>()), Times.AtLeast(3));
        _storage.Verify(x => x.UpdateHeartbeatAsync(It.Is<JobId>(id => id != jobId), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── N2: negative ──────────────────────────────────────────────────────

    /// <summary>N2: a transient storage error is logged as a warning and the next interval still refreshes the heartbeat.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task RunAsync_WhenStorageThrowsOnce_WarnsAndKeepsRefreshing()
    {
        var calls = 0;
        _storage
            .Setup(x => x.UpdateHeartbeatAsync(It.IsAny<JobId>(), It.IsAny<CancellationToken>()))
            .Returns(() => Interlocked.Increment(ref calls) == 1
                ? Task.FromException(new InvalidOperationException("db blip"))
                : Task.CompletedTask);
        using var cts = new CancellationTokenSource();

        var running = Sut.RunAsync(JobId.New(), cts.Token);
        await NexJob.Tests.TestWait.InvokedAsync(_storage, nameof(IJobStorage.UpdateHeartbeatAsync), 3);
        await cts.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        _logger.Warnings.Should().ContainSingle().Which.Should().Contain("Heartbeat update for job");
    }

    /// <summary>N2: a cancellation raised by storage ends the loop quietly, without a warning and without another refresh.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task RunAsync_WhenStorageIsCancelled_EndsWithoutWarningOrFurtherRefresh()
    {
        _storage
            .Setup(x => x.UpdateHeartbeatAsync(It.IsAny<JobId>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Sut.RunAsync(JobId.New(), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        _storage.Verify(x => x.UpdateHeartbeatAsync(It.IsAny<JobId>(), It.IsAny<CancellationToken>()), Times.Once);
        _logger.Warnings.Should().BeEmpty();
    }

    // ─── N3: boundary ──────────────────────────────────────────────────────

    /// <summary>N3: a token that is already cancelled ends the loop at once, with no storage call.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task RunAsync_WithAlreadyCancelledToken_DoesNotTouchStorage()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Sut.RunAsync(JobId.New(), cts.Token).WaitAsync(TimeSpan.FromSeconds(10));

        _storage.Verify(x => x.UpdateHeartbeatAsync(It.IsAny<JobId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>N3: cancelling during the wait ends the loop without a refresh and without an exception.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task RunAsync_CancelledDuringTheWait_EndsWithoutRefreshing()
    {
        _options.HeartbeatInterval = TimeSpan.FromMinutes(5);
        using var cts = new CancellationTokenSource();

        var running = Sut.RunAsync(JobId.New(), cts.Token);
        await cts.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(10));

        _storage.Verify(x => x.UpdateHeartbeatAsync(It.IsAny<JobId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (_warnings)
                {
                    return _warnings.ToArray();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel != LogLevel.Warning)
            {
                return;
            }

            lock (_warnings)
            {
                _warnings.Add(formatter(state, exception));
            }
        }
    }
}
