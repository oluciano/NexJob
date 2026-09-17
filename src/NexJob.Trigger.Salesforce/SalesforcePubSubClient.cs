using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Eventbus.V1;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NexJob.Trigger.Salesforce;

/// <summary>
/// gRPC-based implementation of <see cref="ISalesforcePubSubClient"/> for Salesforce Pub/Sub API.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class SalesforcePubSubClient : ISalesforcePubSubClient
{
    private readonly SalesforceTriggerOptions _options;
    private readonly ISalesforceTokenProvider _tokenProvider;
    private readonly ILogger<SalesforcePubSubClient> _logger;
    private readonly GrpcChannel _channel;
    private readonly PubSub.PubSubClient _client;
    private readonly bool _ownsChannel;

    /// <summary>
    /// Initializes a new instance of the <see cref="SalesforcePubSubClient"/> class.
    /// </summary>
    /// <param name="options">Salesforce trigger options.</param>
    /// <param name="tokenProvider">OAuth2 token provider.</param>
    /// <param name="logger">Diagnostic logger.</param>
    /// <param name="channel">Optional pre-configured gRPC channel (e.g. for testing).</param>
    public SalesforcePubSubClient(
        IOptions<SalesforceTriggerOptions> options,
        ISalesforceTokenProvider tokenProvider,
        ILogger<SalesforcePubSubClient> logger,
        GrpcChannel? channel = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (channel != null)
        {
            _channel = channel;
            _ownsChannel = false;
        }
        else
        {
            var endpoint = _options.PubSubEndpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? _options.PubSubEndpoint
                : "https://" + _options.PubSubEndpoint;

            _channel = GrpcChannel.ForAddress(endpoint);
            _ownsChannel = true;
        }

        _client = new PubSub.PubSubClient(_channel);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ConsumerEvent> SubscribeAsync(
        string topic,
        byte[]? replayId,
        SalesforceReplayPreset replayPreset,
        int batchSize,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var metadata = await CreateCallMetadataAsync(forceRefresh: false, ct).ConfigureAwait(false);

        using var call = _client.Subscribe(metadata, cancellationToken: ct);

        var initialRequest = new FetchRequest
        {
            TopicName = topic,
            NumRequested = batchSize > 0 ? batchSize : 100,
        };

        if (replayId != null && replayId.Length > 0)
        {
            initialRequest.ReplayPreset = ReplayPreset.Custom;
            initialRequest.ReplayId = ByteString.CopyFrom(replayId);
            _logger.LogInformation("Subscribing to topic {Topic} with ReplayPreset CUSTOM (offset length: {Length} bytes)", topic, replayId.Length);
        }
        else
        {
            initialRequest.ReplayPreset = replayPreset switch
            {
                SalesforceReplayPreset.Earliest => ReplayPreset.Earliest,
                SalesforceReplayPreset.Custom when _options.CustomReplayId != null => ReplayPreset.Custom,
                _ => ReplayPreset.Latest,
            };

            if (initialRequest.ReplayPreset == ReplayPreset.Custom && _options.CustomReplayId != null)
            {
                initialRequest.ReplayId = ByteString.CopyFrom(_options.CustomReplayId);
            }

            _logger.LogInformation("Subscribing to topic {Topic} with ReplayPreset {Preset}", topic, initialRequest.ReplayPreset);
        }

        await call.RequestStream.WriteAsync(initialRequest, ct).ConfigureAwait(false);

        while (await call.ResponseStream.MoveNext(ct).ConfigureAwait(false))
        {
            var response = call.ResponseStream.Current;
            var eventsReceived = response.Events.Count;

            if (eventsReceived > 0)
            {
                _logger.LogDebug("Received {Count} events on topic {Topic} (Pending: {Pending})", eventsReceived, topic, response.PendingNumRequested);
                foreach (var consumerEvent in response.Events)
                {
                    yield return consumerEvent;
                }
            }

            // Flow control: Request more events to refill client capacity
            var numToRequest = eventsReceived > 0 ? eventsReceived : batchSize;
            var nextRequest = new FetchRequest
            {
                NumRequested = numToRequest,
            };

            await call.RequestStream.WriteAsync(nextRequest, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<string> GetSchemaJsonAsync(string schemaId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);

        var metadata = await CreateCallMetadataAsync(forceRefresh: false, ct).ConfigureAwait(false);
        var request = new SchemaRequest { SchemaId = schemaId };

        try
        {
            var response = await _client.GetSchemaAsync(request, metadata, cancellationToken: ct).ConfigureAwait(false);
            return response.SchemaJson;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated)
        {
            _logger.LogWarning(ex, "Salesforce Pub/Sub API returned 401 Unauthenticated. Refreshing token and retrying GetSchema...");
            metadata = await CreateCallMetadataAsync(forceRefresh: true, ct).ConfigureAwait(false);
            var response = await _client.GetSchemaAsync(request, metadata, cancellationToken: ct).ConfigureAwait(false);
            return response.SchemaJson;
        }
    }

    /// <inheritdoc/>
    public async Task<TopicInfo> GetTopicInfoAsync(string topic, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var metadata = await CreateCallMetadataAsync(forceRefresh: false, ct).ConfigureAwait(false);
        var request = new TopicRequest { TopicName = topic };

        try
        {
            return await _client.GetTopicAsync(request, metadata, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated)
        {
            _logger.LogWarning(ex, "Salesforce Pub/Sub API returned 401 Unauthenticated. Refreshing token and retrying GetTopic...");
            metadata = await CreateCallMetadataAsync(forceRefresh: true, ct).ConfigureAwait(false);
            return await _client.GetTopicAsync(request, metadata, cancellationToken: ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsChannel)
        {
            _channel.Dispose();
        }
    }

    private async ValueTask<Metadata> CreateCallMetadataAsync(bool forceRefresh, CancellationToken ct)
    {
        var token = await _tokenProvider.GetTokenAsync(forceRefresh, ct).ConfigureAwait(false);

        return new Metadata
        {
            { "accesstoken", token.AccessToken },
            { "instanceurl", token.InstanceUrl },
            { "tenantid", token.TenantId },
        };
    }
}
