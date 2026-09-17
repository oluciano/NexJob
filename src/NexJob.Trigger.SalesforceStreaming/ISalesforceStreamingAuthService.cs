namespace NexJob.Trigger.SalesforceStreaming;

/// <summary>
/// Service responsible for acquiring and refreshing Salesforce authentication tokens.
/// </summary>
public interface ISalesforceStreamingAuthService
{
    /// <summary>
    /// Gets a valid access token and instance URL for Salesforce.
    /// Caches the token in memory and automatically refreshes when required.
    /// </summary>
    /// <param name="options">The authentication options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The acquired token and instance URL.</returns>
    Task<SalesforceStreamingTokenResult> GetTokenAsync(
        SalesforceStreamingAuthOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the currently cached token so that the next request will re-authenticate against Salesforce.
    /// </summary>
    void InvalidateToken();
}
