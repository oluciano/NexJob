using System.Collections.Concurrent;

namespace NexJob.Trigger.SalesforceStreaming;

/// <summary>
/// In-memory implementation of <see cref="IStreamingReplayIdStore"/>.
/// Useful for development, ephemeral testing, or containers where persistence is not required.
/// </summary>
public sealed class InMemoryStreamingReplayIdStore : IStreamingReplayIdStore
{
    private readonly ConcurrentDictionary<string, long> _store = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task<long?> GetReplayIdAsync(string channel, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_store.TryGetValue(channel, out var id) ? (long?)id : null);
    }

    /// <inheritdoc/>
    public Task SaveReplayIdAsync(string channel, long replayId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store[channel] = replayId;
        return Task.CompletedTask;
    }
}
