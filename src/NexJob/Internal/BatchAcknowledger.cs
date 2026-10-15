using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NexJob.Storage;

namespace NexJob.Internal;

/// <summary>
/// Collects the ids of jobs that finished successfully and acknowledges them to storage in batches,
/// instead of one atomic commit per job. Owns the queue, the cancellation source and the flusher task.
/// </summary>
internal sealed class BatchAcknowledger : IDisposable, IAsyncDisposable
{
    private readonly IJobStorage _storage;
    private readonly JobWakeUpChannel _wakeUp;
    private readonly ILogger _logger;
    private readonly Channel<JobId> _channel;
    private readonly CancellationTokenSource _cts;
    private readonly Task _flusherTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="BatchAcknowledger"/> class and starts its flusher.
    /// </summary>
    /// <param name="storage">The job storage that records the acknowledgments.</param>
    /// <param name="wakeUp">The wake-up channel signalled after a batch, so promoted continuations are fetched without waiting a poll.</param>
    /// <param name="logger">The logger used to report a failed flush.</param>
    public BatchAcknowledger(IJobStorage storage, JobWakeUpChannel wakeUp, ILogger logger)
    {
        _storage = storage;
        _wakeUp = wakeUp;
        _logger = logger;
        _channel = Channel.CreateUnbounded<JobId>(new UnboundedChannelOptions { SingleReader = true });
        _cts = new CancellationTokenSource();
        _flusherTask = Task.Run(RunFlusherAsync, _cts.Token);
    }

    /// <summary>
    /// Queues a successfully finished job for the next batch acknowledgment.
    /// </summary>
    /// <param name="jobId">The job to acknowledge.</param>
    public void Enqueue(JobId jobId) => _channel.Writer.TryWrite(jobId);

    /// <inheritdoc/>
    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();
        _cts.Dispose();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _channel.Writer.Complete();
        await _cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _flusherTask.ConfigureAwait(false);
        }
        catch
        {
            // Ignore cancel exceptions during shutdown
        }

        _cts.Dispose();
    }

    private async Task RunFlusherAsync()
    {
        var buffer = new List<JobId>(100);
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                if (await _channel.Reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
                {
                    while (buffer.Count < 100 && _channel.Reader.TryRead(out var id))
                    {
                        buffer.Add(id);
                    }

                    if (buffer.Count > 0)
                    {
                        await _storage.AcknowledgeBatchAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                        _wakeUp.Signal();
                        buffer.Clear();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing batch acknowledgments to storage");
                await Task.Delay(50, CancellationToken.None).ConfigureAwait(false);
            }
        }

        // Drain any leftovers
        while (_channel.Reader.TryRead(out var leftoverId))
        {
            buffer.Add(leftoverId);
        }

        if (buffer.Count > 0)
        {
            await _storage.AcknowledgeBatchAsync(buffer, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
