namespace NexJob.Trigger.SalesforceStreaming;

/// <summary>
/// Specifies the authentication mechanism used to connect to Salesforce Streaming API.
/// </summary>
public enum SalesforceStreamingAuthType
{
    /// <summary>
    /// OAuth 2.0 Username-Password flow (standard for legacy server-to-server integrations).
    /// </summary>
    OAuth2UsernamePassword = 0,

    /// <summary>
    /// OAuth 2.0 Client Credentials flow.
    /// </summary>
    OAuth2ClientCredentials = 1,

    /// <summary>
    /// Direct session or access token with explicit instance URL.
    /// </summary>
    SessionId = 2,
}
