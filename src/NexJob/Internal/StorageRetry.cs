namespace NexJob.Internal;

/// <summary>
/// Retries a storage call that failed once, after each delay of a fixed schedule. One schedule for every write the
/// runtime must not lose to a short outage: the result commit and the release of a continuation.
/// </summary>
internal static class StorageRetry
{
    /// <summary>Gets the default delays: three retries, about 2.6 seconds in all.</summary>
    internal static readonly TimeSpan[] DefaultDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(2),
    ];

    /// <summary>Runs <paramref name="action"/>, retrying after each delay in <paramref name="delays"/>.</summary>
    /// <param name="action">The storage call.</param>
    /// <param name="delays">The wait before each retry.</param>
    /// <param name="onRetry">Called before each wait with the failure, the attempt number and the delay.</param>
    /// <param name="cancellationToken">Stops the wait between two attempts.</param>
    /// <returns>Null when the call succeeded; otherwise the last storage exception.</returns>
    internal static async Task<Exception?> TryAsync(
        Func<Task> action,
        TimeSpan[] delays,
        Action<Exception, int, TimeSpan> onRetry,
        CancellationToken cancellationToken = default)
    {
        Exception? last = null;
        for (var attempt = 0; attempt <= delays.Length; attempt++)
        {
            try
            {
                await action().ConfigureAwait(false);
                return null;
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt < delays.Length)
                {
                    onRetry(ex, attempt + 1, delays[attempt]);
                    await Task.Delay(delays[attempt], cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return last;
    }
}
