using System.Threading.Channels;

namespace NexJob.Internal;

/// <summary>
/// Provides near-zero latency wake-up signaling for immediate dispatch of locally enqueued jobs.
///
/// Uses a bounded channel with capacity=1 and DropWrite mode to collapse multiple rapid signals
/// into a single wake-up, ensuring non-blocking signaling from producer threads.
/// </summary>
internal sealed class JobWakeUpChannel
{
    private readonly Channel<byte> _channel;

    /// <summary>
    /// Initializes a new <see cref="JobWakeUpChannel"/>.
    /// </summary>
    public JobWakeUpChannel()
    {
        var options = new BoundedChannelOptions(capacity: 1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
        };
        _channel = Channel.CreateBounded<byte>(options);
    }

    /// <summary>
    /// Signals the dispatcher that a new job is available.
    ///
    /// Multiple rapid signals collapse into one.
    /// This method never blocks — excess signals are silently dropped.
    /// </summary>
    public void Signal()
    {
        _channel.Writer.TryWrite(default);
    }

    /// <summary>
    /// Waits for a signal or timeout.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for a signal.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> if a signal was received before timeout; <c>false</c> if timeout occurred.
    /// </returns>
    public async ValueTask<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            await _channel.Reader.ReadAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
