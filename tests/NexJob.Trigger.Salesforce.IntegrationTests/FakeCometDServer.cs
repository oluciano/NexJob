using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace NexJob.Trigger.Salesforce.IntegrationTests;

/// <summary>
/// A fake of the Salesforce OAuth token endpoint and of the CometD (Bayeux long-polling) endpoint of the Streaming API.
/// It speaks our reading of the protocol, not Salesforce's: the real service was not used.
/// </summary>
public sealed class FakeCometDServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, byte> _clients = new();
    private readonly ConcurrentQueue<JsonElement> _outbox = new();
    private readonly ConcurrentQueue<long> _subscribedReplayIds = new();
    private int _handshakes;
    private int _handshakesToRefuse;
    private int _nextClient;

    /// <summary>Initializes a new instance of the <see cref="FakeCometDServer"/> class.</summary>
    public FakeCometDServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(System.Net.IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http1));
        _app = builder.Build();
        _app.MapPost("/services/oauth2/token", () => Results.Ok(new
        {
            access_token = "fake-streaming-token",
            instance_url = BaseAddress,
            token_type = "Bearer",
        }));
        _app.MapPost("/cometd/{version}", HandleAsync);
    }

    /// <summary>Gets the base address, also used as the Salesforce instance URL.</summary>
    public string BaseAddress { get; private set; } = string.Empty;

    /// <summary>Gets the OAuth token endpoint.</summary>
    public string AuthEndpoint => $"{BaseAddress}/services/oauth2/token";

    /// <summary>Gets how many handshakes the server answered.</summary>
    public int Handshakes => Volatile.Read(ref _handshakes);

    /// <summary>Gets the replay ids the client sent when it subscribed, in order.</summary>
    public IReadOnlyList<long> SubscribedReplayIds => _subscribedReplayIds.ToArray();

    /// <summary>Starts the server.</summary>
    /// <returns>A task that completes when the server listens.</returns>
    public async Task StartAsync()
    {
        await _app.StartAsync();
        var addresses = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        BaseAddress = addresses.First().TrimEnd('/');
    }

    /// <summary>Queues an event for the next connect call.</summary>
    /// <param name="channel">The channel of the event.</param>
    /// <param name="replayId">The replay id of the event.</param>
    /// <param name="payloadJson">The payload, as JSON.</param>
    public void Publish(string channel, long replayId, string payloadJson)
    {
        var json = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{{\"channel\":\"{channel}\",\"data\":{{\"event\":{{\"replayId\":{replayId},\"createdDate\":\"2026-10-12T12:00:00.000Z\"}},\"payload\":{payloadJson}}}}}");
        using var document = JsonDocument.Parse(json);
        _outbox.Enqueue(document.RootElement.Clone());
    }

    /// <summary>Makes the server forget every client, as when a session expires: the next call gets <c>403::Unknown client</c>.</summary>
    public void ExpireAllSessions() => _clients.Clear();

    /// <summary>Makes the next handshakes fail.</summary>
    /// <param name="count">How many handshakes to refuse.</param>
    public void RefuseNextHandshakes(int count) => Interlocked.Exchange(ref _handshakesToRefuse, count);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task HandleAsync(HttpContext context)
    {
        using var body = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
        var message = body.RootElement[0];
        var channel = message.GetProperty("channel").GetString();
        var clientId = message.TryGetProperty("clientId", out var c) ? c.GetString() : null;
        object[] reply;

        switch (channel)
        {
            case "/meta/handshake":
                if (Interlocked.Decrement(ref _handshakesToRefuse) >= 0)
                {
                    reply = [new { channel, successful = false, error = "handshake refused by the fake" }];
                    break;
                }

                Interlocked.Increment(ref _handshakes);
                var id = $"client-{Interlocked.Increment(ref _nextClient)}";
                _clients[id] = 0;
                reply = [new { channel, successful = true, clientId = id }];
                break;
            case "/meta/subscribe":
                if (clientId is null || !_clients.ContainsKey(clientId))
                {
                    reply = [new { channel, successful = false, error = "403::Unknown client" }];
                    break;
                }

                var subscription = message.GetProperty("subscription").GetString()!;
                _subscribedReplayIds.Enqueue(message.GetProperty("ext").GetProperty("replay").GetProperty(subscription).GetInt64());
                reply = [new { channel, successful = true, subscription }];
                break;
            case "/meta/connect":
                if (clientId is null || !_clients.ContainsKey(clientId))
                {
                    reply = [new { channel, successful = false, error = "403::Unknown client" }];
                    break;
                }

                var events = await DrainAsync(context.RequestAborted);
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    "[" + string.Join(",", events.Select(e => e.GetRawText()).Append("{\"channel\":\"/meta/connect\",\"successful\":true}")) + "]",
                    context.RequestAborted);
                return;
            default:
                reply = [new { channel, successful = true }];
                break;
        }

        await context.Response.WriteAsJsonAsync(reply, context.RequestAborted);
    }

    private async Task<List<JsonElement>> DrainAsync(CancellationToken cancellationToken)
    {
        var events = new List<JsonElement>();
        var until = DateTimeOffset.UtcNow.AddSeconds(1);
        while (DateTimeOffset.UtcNow < until && !cancellationToken.IsCancellationRequested)
        {
            while (_outbox.TryDequeue(out var evt))
            {
                events.Add(evt);
            }

            if (events.Count > 0)
            {
                break;
            }

            await Task.Delay(25, CancellationToken.None);
        }

        return events;
    }
}
