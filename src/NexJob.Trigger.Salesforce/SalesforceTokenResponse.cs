namespace NexJob.Trigger.Salesforce;

/// <summary>
/// Model holding Salesforce OAuth2 session token data.
/// </summary>
public sealed class SalesforceTokenResponse
{
    /// <summary>
    /// Gets or sets the access token string.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Salesforce instance URL (e.g. https://yourinstance.salesforce.com).
    /// </summary>
    public string InstanceUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Salesforce organization/tenant ID.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC instant when this token expires.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
