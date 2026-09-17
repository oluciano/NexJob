using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NexJob.Trigger.SalesforceStreaming;

/// <summary>
/// Default implementation of <see cref="ISalesforceStreamingAuthService"/> using <see cref="HttpClient"/>.
/// </summary>
public sealed class SalesforceStreamingAuthService : ISalesforceStreamingAuthService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SalesforceStreamingAuthService>? _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private SalesforceStreamingTokenResult? _cachedToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="SalesforceStreamingAuthService"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client to use for token requests.</param>
    /// <param name="logger">Optional logger.</param>
    public SalesforceStreamingAuthService(
        HttpClient httpClient,
        ILogger<SalesforceStreamingAuthService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SalesforceStreamingTokenResult> GetTokenAsync(
        SalesforceStreamingAuthOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.AuthType == SalesforceStreamingAuthType.SessionId)
        {
            if (string.IsNullOrWhiteSpace(options.AccessToken) || string.IsNullOrWhiteSpace(options.InstanceUrl))
            {
                throw new SalesforceAuthenticationException("AccessToken and InstanceUrl must not be empty when using SessionId auth.");
            }

            return new SalesforceStreamingTokenResult(options.AccessToken, options.InstanceUrl.TrimEnd('/'));
        }

        if (_cachedToken is not null)
        {
            return _cachedToken;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedToken is not null)
            {
                return _cachedToken;
            }

            _logger?.LogDebug("Requesting Salesforce OAuth token from {AuthEndpoint} using {AuthType}", options.AuthEndpoint, options.AuthType);

            var parameters = new Dictionary<string, string>(StringComparer.Ordinal);

            if (options.AuthType == SalesforceStreamingAuthType.OAuth2UsernamePassword)
            {
                parameters["grant_type"] = "password";
                parameters["client_id"] = options.ClientId ?? string.Empty;
                parameters["client_secret"] = options.ClientSecret ?? string.Empty;
                parameters["username"] = options.Username ?? string.Empty;
                parameters["password"] = (options.Password ?? string.Empty) + (options.SecurityToken ?? string.Empty);
            }
            else if (options.AuthType == SalesforceStreamingAuthType.OAuth2ClientCredentials)
            {
                parameters["grant_type"] = "client_credentials";
                parameters["client_id"] = options.ClientId ?? string.Empty;
                parameters["client_secret"] = options.ClientSecret ?? string.Empty;
            }

            using var content = new FormUrlEncodedContent(parameters);
            using var response = await _httpClient.PostAsync(options.AuthEndpoint, content, cancellationToken).ConfigureAwait(false);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorDescription = ExtractErrorDescription(responseBody);
                throw new SalesforceAuthenticationException(
                    $"Salesforce OAuth request failed with HTTP {(int)response.StatusCode} ({response.StatusCode}): {errorDescription}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (!root.TryGetProperty("access_token", out var tokenProp) || string.IsNullOrWhiteSpace(tokenProp.GetString()))
            {
                throw new SalesforceAuthenticationException("Salesforce OAuth response did not contain a valid 'access_token'.");
            }

            if (!root.TryGetProperty("instance_url", out var urlProp) || string.IsNullOrWhiteSpace(urlProp.GetString()))
            {
                throw new SalesforceAuthenticationException("Salesforce OAuth response did not contain a valid 'instance_url'.");
            }

            var accessToken = tokenProp.GetString()!;
            var instanceUrl = urlProp.GetString()!.TrimEnd('/');

            _cachedToken = new SalesforceStreamingTokenResult(accessToken, instanceUrl);
            _logger?.LogInformation("Successfully acquired Salesforce OAuth token for instance {InstanceUrl}", instanceUrl);

            return _cachedToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public void InvalidateToken()
    {
        _cachedToken = null;
        _logger?.LogDebug("Cached Salesforce OAuth token invalidated.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _lock.Dispose();
    }

    private static string ExtractErrorDescription(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error_description", out var desc))
            {
                return desc.GetString() ?? responseBody;
            }

            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                return err.GetString() ?? responseBody;
            }
        }
        catch (JsonException)
        {
            // fallback to raw body if not valid json
        }

        return responseBody;
    }
}
