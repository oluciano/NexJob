namespace NexJob.Trigger.SalesforceStreaming;

/// <summary>
/// Result of an authentication request against Salesforce.
/// </summary>
/// <param name="AccessToken">The OAuth 2.0 Bearer access token or session ID.</param>
/// <param name="InstanceUrl">The base Salesforce instance URL (e.g. "https://mycompany.my.salesforce.com").</param>
public sealed record SalesforceStreamingTokenResult(string AccessToken, string InstanceUrl);
