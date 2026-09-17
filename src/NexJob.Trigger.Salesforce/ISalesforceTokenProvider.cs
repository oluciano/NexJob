namespace NexJob.Trigger.Salesforce;

/// <summary>
/// Provider contract for retrieving and caching Salesforce OAuth2 tokens.
/// </summary>
public interface ISalesforceTokenProvider
{
    /// <summary>
    /// Gets an active Salesforce session token, automatically refreshing if expired.
    /// </summary>
    /// <param name="forceRefresh">When true, ignores cached token and requests a fresh token.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The token response containing AccessToken, InstanceUrl, and TenantId.</returns>
    ValueTask<SalesforceTokenResponse> GetTokenAsync(bool forceRefresh = false, CancellationToken ct = default);
}
