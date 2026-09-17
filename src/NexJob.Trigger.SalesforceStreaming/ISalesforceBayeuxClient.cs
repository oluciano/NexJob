namespace NexJob.Trigger.SalesforceStreaming;

/// <summary>
/// Client interface responsible for Bayeux/CometD protocol interactions with the Salesforce Streaming API.
/// </summary>
public interface ISalesforceBayeuxClient : IDisposable
{
    /// <summary>
    /// Performs the initial Bayeux handshake with the Salesforce CometD server.
    /// </summary>
    /// <param name="instanceUrl">The Salesforce instance URL.</param>
    /// <param name="cometdVersion">The CometD API version (e.g. "60.0").</param>
    /// <param name="accessToken">The Bearer access token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The assigned Bayeux client ID.</returns>
    Task<string> HandshakeAsync(
        string instanceUrl,
        string cometdVersion,
        string accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to the specified streaming channel with a starting Replay ID.
    /// </summary>
    /// <param name="instanceUrl">The Salesforce instance URL.</param>
    /// <param name="cometdVersion">The CometD API version.</param>
    /// <param name="accessToken">The Bearer access token.</param>
    /// <param name="clientId">The Bayeux client ID returned by the handshake.</param>
    /// <param name="channel">The channel name (e.g. "/data/Order__ChangeEvent").</param>
    /// <param name="replayId">The Replay ID (-1, -2, or custom positive ID).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if subscription succeeded.</returns>
    Task<bool> SubscribeAsync(
        string instanceUrl,
        string cometdVersion,
        string accessToken,
        string clientId,
        string channel,
        long replayId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a long-polling /meta/connect request to receive published events and heartbeats from Salesforce.
    /// </summary>
    /// <param name="instanceUrl">The Salesforce instance URL.</param>
    /// <param name="cometdVersion">The CometD API version.</param>
    /// <param name="accessToken">The Bearer access token.</param>
    /// <param name="clientId">The Bayeux client ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of event inputs received during this connect poll cycle.</returns>
    Task<IReadOnlyList<SalesforceStreamingEventInput>> ConnectAsync(
        string instanceUrl,
        string cometdVersion,
        string accessToken,
        string clientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gracefully disconnects the Bayeux client session from the CometD server.
    /// </summary>
    /// <param name="instanceUrl">The Salesforce instance URL.</param>
    /// <param name="cometdVersion">The CometD API version.</param>
    /// <param name="accessToken">The Bearer access token.</param>
    /// <param name="clientId">The Bayeux client ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the disconnect operation.</returns>
    Task DisconnectAsync(
        string instanceUrl,
        string cometdVersion,
        string accessToken,
        string clientId,
        CancellationToken cancellationToken = default);
}
