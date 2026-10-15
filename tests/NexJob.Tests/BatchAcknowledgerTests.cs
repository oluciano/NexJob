using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NexJob.Storage;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Batch acknowledgment of finished jobs: queued ids are written to storage in batches, a failed flush keeps its
/// ids for the next one, and disposing drains what is still queued.
/// </summary>
public sealed class BatchAcknowledgerTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private readonly Mock<IJobStorage> _storage = new();
    private readonly JobWakeUpChannel _wakeUp = new();
    private readonly RecordingLogger _logger = new();
    private readonly List<JobId[]> _batches = [];

    // ─── N1: positive ──────────────────────────────────────────────────────

    /// <summary>N1: a queued job is acknowledged in a batch and the dispatcher is woken afterwards.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Enqueue_QueuedJob_IsAcknowledgedInABatchAndWakesTheDispatcher()
    {
        RecordBatches();
        var jobId = JobId.New();
        await using var sut = Create();

        sut.Enqueue(jobId);
        await WaitForBatchesAsync(1);

        Batches().Should().ContainSingle().Which.Should().Equal(jobId);
        (await _wakeUp.WaitAsync(Bound, CancellationToken.None)).Should().BeTrue();
    }

    /// <summary>N1: more ids than one batch holds are split at the limit, each id is written once, and none is repeated.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Enqueue_MoreThanOneBatch_SplitsAtTheLimitWithoutRepeatingIds()
    {
        var firstFlushStarted = new TaskCompletionSource();
        var releaseFirstFlush = new TaskCompletionSource();
        RecordBatches(async call =>
        {
            if (call == 1)
            {
                firstFlushStarted.SetResult();
                await releaseFirstFlush.Task;
            }
        });
        var ids = Enumerable.Range(0, 251).Select(_ => JobId.New()).ToArray();
        await using var sut = Create();

        sut.Enqueue(ids[0]);
        await firstFlushStarted.Task.WaitAsync(Bound);
        foreach (var id in ids.Skip(1))
        {
            sut.Enqueue(id); // queued while the flusher is busy with the first batch
        }

        releaseFirstFlush.SetResult();
        await NexJob.Tests.TestWait.UntilAsync(() => Task.FromResult(Batches().Sum(b => b.Length) >= ids.Length));

        var batches = Batches();
        batches.Max(b => b.Length).Should().Be(100);
        batches.SelectMany(b => b).Should().Equal(ids);
    }

    /// <summary>N1: ids still queued when the acknowledger is disposed are written before the dispose returns.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task DisposeAsync_WithQueuedIds_DrainsThemBeforeReturning()
    {
        var firstFlushStarted = new TaskCompletionSource();
        var releaseFirstFlush = new TaskCompletionSource();
        RecordBatches(async call =>
        {
            if (call == 1)
            {
                firstFlushStarted.SetResult();
                await releaseFirstFlush.Task;
            }
        });
        var first = JobId.New();
        var second = JobId.New();
        var sut = Create();

        sut.Enqueue(first);
        await firstFlushStarted.Task.WaitAsync(Bound);
        sut.Enqueue(second);
        var disposing = sut.DisposeAsync().AsTask();
        releaseFirstFlush.SetResult();
        await disposing.WaitAsync(Bound);

        Batches().Should().HaveCount(2);
        Batches()[0].Should().Equal(first);
        Batches()[1].Should().Equal(second);
    }

    // ─── N2: negative ──────────────────────────────────────────────────────

    /// <summary>N2: a failed flush is logged as an error and its ids are kept, so the next flush writes them with the new ones.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Enqueue_WhenAFlushFails_LogsAndRetriesTheSameIdsWithTheNextBatch()
    {
        var failFirstCall = true;
        _storage
            .Setup(x => x.AcknowledgeBatchAsync(It.IsAny<IReadOnlyList<JobId>>(), It.IsAny<CancellationToken>()))
            .Returns<IReadOnlyList<JobId>, CancellationToken>((ids, _) =>
            {
                lock (_batches)
                {
                    _batches.Add(ids.ToArray());
                }

                if (failFirstCall)
                {
                    failFirstCall = false;
                    return Task.FromException(new InvalidOperationException("db blip"));
                }

                return Task.CompletedTask;
            });
        var first = JobId.New();
        var second = JobId.New();
        await using var sut = Create();

        sut.Enqueue(first);
        await NexJob.Tests.TestWait.UntilAsync(() => Task.FromResult(_logger.Errors.Count == 1));
        sut.Enqueue(second);
        await WaitForBatchesAsync(2);

        Batches()[0].Should().Equal(first);
        Batches()[1].Should().Equal(first, second);
        _logger.Errors.Should().ContainSingle().Which.Should().Contain("Error flushing batch acknowledgments");
    }

    /// <summary>N2: when the final drain fails, disposing still completes without throwing.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task DisposeAsync_WhenTheFinalDrainFails_DoesNotThrow()
    {
        var firstFlushStarted = new TaskCompletionSource();
        var releaseFirstFlush = new TaskCompletionSource();
        RecordBatches(async call =>
        {
            if (call == 1)
            {
                firstFlushStarted.SetResult();
                await releaseFirstFlush.Task;
            }
            else
            {
                throw new InvalidOperationException("db down");
            }
        });
        var sut = Create();

        sut.Enqueue(JobId.New());
        await firstFlushStarted.Task.WaitAsync(Bound);
        sut.Enqueue(JobId.New());
        var disposing = sut.DisposeAsync().AsTask();
        releaseFirstFlush.SetResult();

        await FluentActions.Awaiting(() => disposing.WaitAsync(Bound)).Should().NotThrowAsync();
        Batches().Should().HaveCount(2);
    }

    // ─── N3: boundary ──────────────────────────────────────────────────────

    /// <summary>N3: disposing with nothing queued writes no acknowledgment.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task DisposeAsync_WithNothingQueued_DoesNotAcknowledge()
    {
        RecordBatches();
        var sut = Create();

        await sut.DisposeAsync();

        Batches().Should().BeEmpty();
    }

    /// <summary>N3: an id queued after the acknowledger was disposed is never written.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Enqueue_AfterDisposeAsync_IsNeverAcknowledged()
    {
        RecordBatches();
        var sut = Create();
        await sut.DisposeAsync();

        sut.Enqueue(JobId.New());

        await Task.Yield();
        Batches().Should().BeEmpty();
    }

    private BatchAcknowledger Create() => new(_storage.Object, _wakeUp, _logger);

    private void RecordBatches(Func<int, Task>? onCall = null)
    {
        _storage
            .Setup(x => x.AcknowledgeBatchAsync(It.IsAny<IReadOnlyList<JobId>>(), It.IsAny<CancellationToken>()))
            .Returns<IReadOnlyList<JobId>, CancellationToken>(async (ids, _) =>
            {
                int call;
                lock (_batches)
                {
                    _batches.Add(ids.ToArray()); // the acknowledger reuses and clears its buffer, so copy at call time
                    call = _batches.Count;
                }

                if (onCall is not null)
                {
                    await onCall(call);
                }
            });
    }

    private JobId[][] Batches()
    {
        lock (_batches)
        {
            return _batches.ToArray();
        }
    }

    private Task WaitForBatchesAsync(int count)
        => NexJob.Tests.TestWait.UntilAsync(() => Task.FromResult(Batches().Length >= count));

    private sealed class RecordingLogger : ILogger
    {
        private readonly List<string> _errors = [];

        public IReadOnlyList<string> Errors
        {
            get
            {
                lock (_errors)
                {
                    return _errors.ToArray();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel != LogLevel.Error)
            {
                return;
            }

            lock (_errors)
            {
                _errors.Add(formatter(state, exception));
            }
        }
    }
}
