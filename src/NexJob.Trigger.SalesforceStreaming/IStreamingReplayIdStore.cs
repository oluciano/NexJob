namespace NexJob.Trigger.SalesforceStreaming;

/// <summary>
/// Defines a contract for persisting and retrieving Salesforce Streaming API Replay IDs across application restarts.
/// </summary>
public interface IStreamingReplayIdStore
{
    /// <summary>
    /// Gets the last committed Replay ID for the specified channel, or null if no Replay ID has been stored yet.
    /// </summary>
    /// <param name="channel">The Salesforce streaming channel name (e.g., "/data/Order__ChangeEvent").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored Replay ID, or null if not found.</returns>
    Task<long?> GetReplayIdAsync(string channel, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the last committed Replay ID for the specified channel.
    /// </summary>
    /// <param name="channel">The Salesforce streaming channel name.</param>
    /// <param name="replayId">The Replay ID to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous save operation.</returns>
    Task SaveReplayIdAsync(string channel, long replayId, CancellationToken cancellationToken = default);
}
