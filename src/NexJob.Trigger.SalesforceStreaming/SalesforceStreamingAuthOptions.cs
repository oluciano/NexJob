using System.ComponentModel.DataAnnotations;

namespace NexJob.Trigger.SalesforceStreaming;

/// <summary>
/// Authentication settings for connecting to the Salesforce Streaming API.
/// </summary>
public sealed class SalesforceStreamingAuthOptions : IValidatableObject
{
    private const string InstanceUrlMessage =
        "InstanceUrl must be an absolute https URL when using SessionId authentication (http is accepted only for a loopback address): the access token is sent to it.";

    private const string AuthEndpointMessage =
        "AuthEndpoint must be an absolute https URL (http is accepted only for a loopback address): the OAuth credentials are sent to it.";

    /// <summary>
    /// Gets or sets the authentication type to use. Defaults to <see cref="SalesforceStreamingAuthType.OAuth2UsernamePassword"/>.
    /// </summary>
    public SalesforceStreamingAuthType AuthType { get; set; } = SalesforceStreamingAuthType.OAuth2UsernamePassword;

    /// <summary>
    /// Gets or sets the OAuth 2.0 token endpoint URL.
    /// Defaults to "https://login.salesforce.com/services/oauth2/token".
    /// </summary>
    public string AuthEndpoint { get; set; } = "https://login.salesforce.com/services/oauth2/token";

    /// <summary>
    /// Gets or sets the Salesforce Connected App Client ID (Consumer Key).
    /// Required for <see cref="SalesforceStreamingAuthType.OAuth2UsernamePassword"/> and <see cref="SalesforceStreamingAuthType.OAuth2ClientCredentials"/>.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the Salesforce Connected App Client Secret (Consumer Secret).
    /// Required for <see cref="SalesforceStreamingAuthType.OAuth2UsernamePassword"/> and <see cref="SalesforceStreamingAuthType.OAuth2ClientCredentials"/>.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the Salesforce integration user username.
    /// Required for <see cref="SalesforceStreamingAuthType.OAuth2UsernamePassword"/>.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Gets or sets the Salesforce integration user password.
    /// Required for <see cref="SalesforceStreamingAuthType.OAuth2UsernamePassword"/>.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets the Salesforce user security token (appended to password if configured in Salesforce).
    /// Optional.
    /// </summary>
    public string? SecurityToken { get; set; }

    /// <summary>
    /// Gets or sets the Salesforce base instance URL (e.g., "https://na1.salesforce.com" or "https://mycompany.my.salesforce.com").
    /// Required when using <see cref="SalesforceStreamingAuthType.SessionId"/>.
    /// </summary>
    public string? InstanceUrl { get; set; }

    /// <summary>
    /// Gets or sets the direct Session ID or pre-generated OAuth Bearer access token.
    /// Required when using <see cref="SalesforceStreamingAuthType.SessionId"/>.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>
    /// Gets or sets an alias for <see cref="AccessToken"/> for compatibility with legacy Salesforce Session ID naming.
    /// </summary>
    public string? SessionId
    {
        get => AccessToken;
        set => AccessToken = value;
    }

    /// <summary>
    /// Validates the authentication options according to the selected <see cref="AuthType"/>.
    /// </summary>
    /// <param name="validationContext">Validation context.</param>
    /// <returns>Collection of validation results.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        switch (AuthType)
        {
            case SalesforceStreamingAuthType.OAuth2UsernamePassword:
                if (!IsHttpsOrLoopbackHttp(AuthEndpoint))
                {
                    yield return new ValidationResult(
                        AuthEndpointMessage,
                        new[] { nameof(AuthEndpoint), });
                }

                if (string.IsNullOrWhiteSpace(ClientId))
                {
                    yield return new ValidationResult(
                        "ClientId is required for OAuth2UsernamePassword authentication.",
                        new[] { nameof(ClientId), });
                }

                if (string.IsNullOrWhiteSpace(ClientSecret))
                {
                    yield return new ValidationResult(
                        "ClientSecret is required for OAuth2UsernamePassword authentication.",
                        new[] { nameof(ClientSecret), });
                }

                if (string.IsNullOrWhiteSpace(Username))
                {
                    yield return new ValidationResult(
                        "Username is required for OAuth2UsernamePassword authentication.",
                        new[] { nameof(Username), });
                }

                if (string.IsNullOrWhiteSpace(Password))
                {
                    yield return new ValidationResult(
                        "Password is required for OAuth2UsernamePassword authentication.",
                        new[] { nameof(Password), });
                }

                break;

            case SalesforceStreamingAuthType.OAuth2ClientCredentials:
                if (!IsHttpsOrLoopbackHttp(AuthEndpoint))
                {
                    yield return new ValidationResult(
                        AuthEndpointMessage,
                        new[] { nameof(AuthEndpoint), });
                }

                if (string.IsNullOrWhiteSpace(ClientId))
                {
                    yield return new ValidationResult(
                        "ClientId is required for OAuth2ClientCredentials authentication.",
                        new[] { nameof(ClientId), });
                }

                if (string.IsNullOrWhiteSpace(ClientSecret))
                {
                    yield return new ValidationResult(
                        "ClientSecret is required for OAuth2ClientCredentials authentication.",
                        new[] { nameof(ClientSecret), });
                }

                break;

            case SalesforceStreamingAuthType.SessionId:
                if (!IsHttpsOrLoopbackHttp(InstanceUrl))
                {
                    yield return new ValidationResult(
                        InstanceUrlMessage,
                        new[] { nameof(InstanceUrl), });
                }

                if (string.IsNullOrWhiteSpace(AccessToken))
                {
                    yield return new ValidationResult(
                        "AccessToken is required when using SessionId authentication.",
                        new[] { nameof(AccessToken), });
                }

                break;
        }
    }

    private static bool IsHttpsOrLoopbackHttp(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))));
}
