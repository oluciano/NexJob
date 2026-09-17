using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NexJob.Trigger.Salesforce;

/// <summary>
/// Default implementation of <see cref="ISalesforceTokenProvider"/> supporting Client Credentials flow and caching.
/// </summary>
public sealed class SalesforceTokenProvider : ISalesforceTokenProvider, IDisposable
{
    private readonly SalesforceTriggerOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ILogger<SalesforceTokenProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private SalesforceTokenResponse? _cachedToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="SalesforceTokenProvider"/> class.
    /// </summary>
    /// <param name="options">Salesforce trigger options.</param>
    /// <param name="httpClient">HTTP client used for token requests.</param>
    /// <param name="logger">Diagnostic logger.</param>
    public SalesforceTokenProvider(
        IOptions<SalesforceTriggerOptions> options,
        HttpClient httpClient,
        ILogger<SalesforceTokenProvider> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async ValueTask<SalesforceTokenResponse> GetTokenAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        var current = _cachedToken;
        if (!forceRefresh && current != null && current.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(60))
        {
            return current;
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-checked locking pattern
            current = _cachedToken;
            if (!forceRefresh && current != null && current.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(60))
            {
                return current;
            }

            _logger.LogInformation("Requesting fresh Salesforce OAuth2 access token from {Endpoint}...", _options.AuthEndpoint);

            var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
            };

            using var content = new FormUrlEncodedContent(parameters);
            using var response = await _httpClient.PostAsync(_options.AuthEndpoint, content, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogError("Salesforce OAuth2 authentication failed with status {StatusCode}: {Error}", response.StatusCode, errorBody);
                throw new SalesforceAuthenticationException($"OAuth2 authentication failed ({response.StatusCode}): {errorBody}");
            }

            var tokenPayload = await response.Content.ReadFromJsonAsync<SalesforceAuthResponseDto>(cancellationToken: ct).ConfigureAwait(false)
                ?? throw new SalesforceAuthenticationException("Salesforce returned empty authentication response.");

            var tenantId = _options.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId) && !string.IsNullOrWhiteSpace(tokenPayload.Id))
            {
                tenantId = ExtractTenantIdFromIdUrl(tokenPayload.Id);
            }

            var expiresInSeconds = tokenPayload.ExpiresIn > 0 ? tokenPayload.ExpiresIn : 7200; // default 2 hours
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);

            var token = new SalesforceTokenResponse
            {
                AccessToken = tokenPayload.AccessToken,
                InstanceUrl = tokenPayload.InstanceUrl ?? string.Empty,
                TenantId = tenantId ?? string.Empty,
                ExpiresAt = expiresAt,
            };

            _cachedToken = token;
            _logger.LogInformation("Successfully acquired Salesforce access token. Expires at: {ExpiresAt}", expiresAt);
            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _lock.Dispose();
    }

    private static string? ExtractTenantIdFromIdUrl(string idUrl)
    {
        // Format: https://login.salesforce.com/id/{orgId}/{userId}
        if (Uri.TryCreate(idUrl, UriKind.Absolute, out var uri))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var idIndex = Array.IndexOf(segments, "id");
            if (idIndex >= 0 && idIndex + 1 < segments.Length)
            {
                return segments[idIndex + 1];
            }
        }

        return null;
    }

    private sealed class SalesforceAuthResponseDto
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("instance_url")]
        public string? InstanceUrl { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
