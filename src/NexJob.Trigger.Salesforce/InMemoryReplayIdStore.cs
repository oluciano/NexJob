using System.Collections.Concurrent;

namespace NexJob.Trigger.Salesforce;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IReplayIdStore"/>, suitable for testing and ephemeral workloads.
/// </summary>
public sealed class InMemoryReplayIdStore : IReplayIdStore
{
    private readonly ConcurrentDictionary<string, byte[]> _store = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public ValueTask<byte[]?> GetLastReplayIdAsync(string topic, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        _store.TryGetValue(topic, out var replayId);
        return ValueTask.FromResult(replayId);
    }

    /// <inheritdoc/>
    public ValueTask SaveReplayIdAsync(string topic, byte[] replayId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(replayId);

        _store[topic] = (byte[])replayId.Clone();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Clears all stored replay IDs.
    /// </summary>
    public void Clear()
    {
        _store.Clear();
    }
}
