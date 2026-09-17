namespace NexJob.Trigger.Salesforce;

/// <summary>
/// Contract for persisting and retrieving opaque binary Replay IDs for Salesforce topics.
/// </summary>
public interface IReplayIdStore
{
    /// <summary>
    /// Retrieves the last persisted Replay ID for the specified topic.
    /// </summary>
    /// <param name="topic">The Salesforce topic name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The Replay ID bytes, or null if no offset has been saved.</returns>
    ValueTask<byte[]?> GetLastReplayIdAsync(string topic, CancellationToken ct = default);

    /// <summary>
    /// Persists the Replay ID for the specified topic.
    /// </summary>
    /// <param name="topic">The Salesforce topic name.</param>
    /// <param name="replayId">The opaque binary Replay ID bytes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous save operation.</returns>
    ValueTask SaveReplayIdAsync(string topic, byte[] replayId, CancellationToken ct = default);
}
