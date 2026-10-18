namespace NexJob.IntegrationTests;

/// <summary>
/// A host with a <see cref="SynchronizationContext"/> that runs everything on one thread (WinForms, WPF, a legacy
/// ASP.NET host). The continuation of an <c>await</c> that does not use <c>ConfigureAwait(false)</c> is posted back to
/// it, so blocking that thread on the awaited work deadlocks.
/// </summary>
internal sealed class SingleThreadedSynchronizationContext : SynchronizationContext
{
    private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

    /// <summary>
    /// Runs <paramref name="work"/> on a thread that pumps this context and returns its result. A deadlock surfaces as a
    /// <see cref="TimeoutException"/> after <paramref name="timeout"/>; the stuck pump thread is a background thread.
    /// </summary>
    public static T Run<T>(Func<T> work, TimeSpan timeout)
    {
        var context = new SingleThreadedSynchronizationContext();
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            SetSynchronizationContext(context);
            context.Post(
                _ =>
                {
                    try
                    {
                        result.TrySetResult(work());
                    }
                    catch (Exception ex)
                    {
                        result.TrySetException(ex);
                    }
                },
                null);
            foreach (var (callback, state) in context._queue.GetConsumingEnumerable())
            {
                callback(state);
            }
        })
        {
            IsBackground = true,
        };
        thread.Start();

        if (!result.Task.Wait(timeout))
        {
            throw new TimeoutException($"The work did not finish in {timeout}: it is blocked on a continuation posted to the synchronization context.");
        }

        context._queue.CompleteAdding();
        return result.Task.GetAwaiter().GetResult();
    }

    /// <inheritdoc/>
    public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));
}
