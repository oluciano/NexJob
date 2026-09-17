using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NexJob.Trigger.SalesforceStreaming;

/// <summary>
/// Implementation of <see cref="ISalesforceBayeuxClient"/> implementing the Bayeux protocol over HTTP long-polling.
/// </summary>
public sealed class SalesforceBayeuxClient : ISalesforceBayeuxClient
{
    private const string JsonContentType = "application/json";
    private readonly HttpClient _httpClient;
    private readonly ILogger<SalesforceBayeuxClient>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SalesforceBayeuxClient"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client to use for Bayeux communication.</param>
    /// <param name="logger">Optional logger.</param>
    public SalesforceBayeuxClient(
        HttpClient httpClient,
        ILogger<SalesforceBayeuxClient>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> HandshakeAsync(
        string instanceUrl,
        string cometdVersion,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(cometdVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        var endpoint = BuildEndpoint(instanceUrl, cometdVersion);

        var requestPayload = new[]
        {
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["channel"] = "/meta/handshake",
                ["version"] = "1.0",
                ["minimumVersion"] = "1.0",
                ["supportedConnectionTypes"] = new[] { "long-polling", },
            },
        };

        var responseArray = await SendBayeuxRequestAsync(endpoint, requestPayload, accessToken, cancellationToken).ConfigureAwait(false);

        foreach (var message in responseArray.EnumerateArray())
        {
            if (message.TryGetProperty("channel", out var ch) && string.Equals(ch.GetString(), "/meta/handshake", StringComparison.Ordinal))
            {
                var successful = message.TryGetProperty("successful", out var succ) && succ.GetBoolean();
                if (!successful)
                {
                    var error = message.TryGetProperty("error", out var err) ? err.GetString() : "Handshake failed";
                    throw new SalesforceBayeuxException($"Salesforce Bayeux handshake failed: {error}", error, shouldRehandshake: true);
                }

                if (message.TryGetProperty("clientId", out var cid) && !string.IsNullOrWhiteSpace(cid.GetString()))
                {
                    var clientId = cid.GetString()!;
                    _logger?.LogDebug("Bayeux handshake succeeded with clientId: {ClientId}", clientId);
                    return clientId;
                }
            }
        }

        throw new SalesforceBayeuxException("Salesforce Bayeux handshake response did not contain a valid clientId.");
    }

    /// <inheritdoc/>
    public async Task<bool> SubscribeAsync(
        string instanceUrl,
        string cometdVersion,
        string accessToken,
        string clientId,
        string channel,
        long replayId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(cometdVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        var endpoint = BuildEndpoint(instanceUrl, cometdVersion);

        var replayDict = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            [channel] = replayId,
        };

        var extDict = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["replay"] = replayDict,
        };

        var requestPayload = new[]
        {
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["channel"] = "/meta/subscribe",
                ["clientId"] = clientId,
                ["subscription"] = channel,
                ["ext"] = extDict,
            },
        };

        _logger?.LogDebug(
            "Subscribing to channel '{Channel}' with Replay ID {ReplayId} for client {ClientId}",
            channel,
            replayId,
            clientId);

        var responseArray = await SendBayeuxRequestAsync(endpoint, requestPayload, accessToken, cancellationToken).ConfigureAwait(false);

        foreach (var message in responseArray.EnumerateArray())
        {
            if (message.TryGetProperty("channel", out var ch) && string.Equals(ch.GetString(), "/meta/subscribe", StringComparison.Ordinal))
            {
                var successful = message.TryGetProperty("successful", out var succ) && succ.GetBoolean();
                if (!successful)
                {
                    var error = message.TryGetProperty("error", out var err) ? err.GetString() : "Subscribe failed";
                    var shouldRehandshake = IsSessionExpiredError(error);
                    throw new SalesforceBayeuxException(
                        $"Salesforce Bayeux subscribe to '{channel}' failed: {error}",
                        error,
                        shouldRehandshake);
                }

                _logger?.LogInformation("Successfully subscribed to Salesforce channel '{Channel}'", channel);
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SalesforceStreamingEventInput>> ConnectAsync(
        string instanceUrl,
        string cometdVersion,
        string accessToken,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(cometdVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var endpoint = BuildEndpoint(instanceUrl, cometdVersion);

        var requestPayload = new[]
        {
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["channel"] = "/meta/connect",
                ["clientId"] = clientId,
                ["connectionType"] = "long-polling",
            },
        };

        var responseArray = await SendBayeuxRequestAsync(endpoint, requestPayload, accessToken, cancellationToken).ConfigureAwait(false);
        var events = new List<SalesforceStreamingEventInput>();

        foreach (var message in responseArray.EnumerateArray())
        {
            if (!message.TryGetProperty("channel", out var chProp) || string.IsNullOrWhiteSpace(chProp.GetString()))
            {
                continue;
            }

            var messageChannel = chProp.GetString()!;

            if (string.Equals(messageChannel, "/meta/connect", StringComparison.Ordinal))
            {
                var successful = message.TryGetProperty("successful", out var succ) && succ.GetBoolean();
                if (!successful)
                {
                    var error = message.TryGetProperty("error", out var err) ? err.GetString() : "Connect failed";
                    var shouldRehandshake = IsSessionExpiredError(error);
                    throw new SalesforceBayeuxException(
                        $"Salesforce Bayeux connect failed: {error}",
                        error,
                        shouldRehandshake);
                }
            }
            else if (!messageChannel.StartsWith("/meta/", StringComparison.Ordinal)
                     && TryParseEventMessage(messageChannel, message, out var eventInput)
                     && eventInput is not null)
            {
                events.Add(eventInput);
            }
        }

        return events;
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync(
        string instanceUrl,
        string cometdVersion,
        string accessToken,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceUrl) || string.IsNullOrWhiteSpace(clientId))
        {
            return;
        }

        try
        {
            var endpoint = BuildEndpoint(instanceUrl, cometdVersion);
            var requestPayload = new[]
            {
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["channel"] = "/meta/disconnect",
                    ["clientId"] = clientId,
                },
            };

            await SendBayeuxRequestAsync(endpoint, requestPayload, accessToken, cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug("Gracefully disconnected Bayeux client {ClientId}", clientId);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Ignored error during Bayeux disconnect");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // HttpClient lifetime is managed by caller / DI container
    }

    private static string BuildEndpoint(string instanceUrl, string cometdVersion)
    {
        return $"{instanceUrl.TrimEnd('/')}/cometd/{cometdVersion.TrimStart('v')}";
    }

    private static bool IsSessionExpiredError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        return error.Contains("403::", StringComparison.OrdinalIgnoreCase)
            || error.Contains("401::", StringComparison.OrdinalIgnoreCase)
            || error.Contains("402::", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Unknown client", StringComparison.OrdinalIgnoreCase)
            || error.Contains("invalid client_id", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseEventMessage(
        string channel,
        JsonElement message,
        out SalesforceStreamingEventInput? eventInput)
    {
        eventInput = null;

        if (!message.TryGetProperty("data", out var dataElement))
        {
            return false;
        }

        long replayId = 0;
        var createdDate = DateTimeOffset.UtcNow;
        string? schema = null;
        string? eventId = null;

        if (dataElement.TryGetProperty("schema", out var schemaProp))
        {
            schema = schemaProp.GetString();
        }

        if (dataElement.TryGetProperty("event", out var eventMeta))
        {
            if (eventMeta.TryGetProperty("replayId", out var replayProp))
            {
                if (replayProp.ValueKind == JsonValueKind.Number && replayProp.TryGetInt64(out var rId))
                {
                    replayId = rId;
                }
                else if (replayProp.ValueKind == JsonValueKind.String &&
                         long.TryParse(replayProp.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId))
                {
                    replayId = parsedId;
                }
            }

            if (eventMeta.TryGetProperty("createdDate", out var dateProp) &&
                DateTimeOffset.TryParse(dateProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                createdDate = parsedDate;
            }

            if (eventMeta.TryGetProperty("eventId", out var eventIdProp))
            {
                eventId = eventIdProp.GetString();
            }
        }

        var payload = dataElement.TryGetProperty("payload", out var payloadElement)
            ? payloadElement.Clone()
            : dataElement.Clone();

        if (string.IsNullOrWhiteSpace(eventId)
            && payload.TryGetProperty("ChangeEventHeader", out var header)
            && header.TryGetProperty("transactionKey", out var txKey))
        {
            eventId = txKey.GetString();
        }

        eventInput = new SalesforceStreamingEventInput(
            ReplayId: replayId,
            Channel: channel,
            Payload: payload,
            CreatedDate: createdDate,
            EventId: eventId,
            Schema: schema);

        return true;
    }

    private async Task<JsonElement> SendBayeuxRequestAsync(
        string endpoint,
        object requestPayload,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(requestPayload);
        using var content = new StringContent(json, Encoding.UTF8, JsonContentType);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = content,
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var isAuthFailure = (int)response.StatusCode == 401 || (int)response.StatusCode == 403;
            throw new SalesforceBayeuxException(
                $"Salesforce CometD request to '{endpoint}' failed with HTTP {(int)response.StatusCode} ({response.StatusCode}): {responseBody}",
                errorCode: response.StatusCode.ToString(),
                shouldRehandshake: isAuthFailure);
        }

        using var document = JsonDocument.Parse(responseBody);
        return document.RootElement.Clone();
    }
}
