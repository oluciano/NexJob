using System.Globalization;
using System.Net;
using System.Text;
using FluentAssertions;
using Xunit;

namespace NexJob.Trigger.SalesforceStreaming.Tests;

public sealed class SalesforceBayeuxClientTests
{
    private const string InstanceUrl = "https://na1.salesforce.com";
    private const string CometdVersion = "60.0";
    private const string AccessToken = "test-token";

    [Fact]
    public async Task HandshakeAsync_SuccessfulResponse_ReturnsClientId()
    {
        // Arrange
        var responseJson = """
        [
            {
                "channel": "/meta/handshake",
                "clientId": "client-abc-123",
                "successful": true,
                "supportedConnectionTypes": ["long-polling"]
            }
        ]
        """;

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            req.RequestUri!.ToString().Should().Be("https://na1.salesforce.com/cometd/60.0");
            req.Headers.Authorization!.Scheme.Should().Be("Bearer");
            req.Headers.Authorization.Parameter.Should().Be(AccessToken);

            var body = await req.Content!.ReadAsStringAsync(ct);
            body.Should().Contain("/meta/handshake");
            body.Should().Contain("long-polling");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        });

        using var client = new SalesforceBayeuxClient(new HttpClient(handler));

        // Act
        var clientId = await client.HandshakeAsync(InstanceUrl, CometdVersion, AccessToken);

        // Assert
        clientId.Should().Be("client-abc-123");
    }

    [Fact]
    public async Task HandshakeAsync_UnsuccessfulResponse_ThrowsSalesforceBayeuxException()
    {
        // Arrange
        var responseJson = """
        [
            {
                "channel": "/meta/handshake",
                "successful": false,
                "error": "401::Authentication invalid"
            }
        ]
        """;

        var handler = new MockHttpMessageHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        }));

        using var client = new SalesforceBayeuxClient(new HttpClient(handler));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<SalesforceBayeuxException>(() =>
            client.HandshakeAsync(InstanceUrl, CometdVersion, AccessToken));

        ex.ShouldRehandshake.Should().BeTrue();
        ex.Message.Should().Contain("401::Authentication invalid");
    }

    [Fact]
    public async Task SubscribeAsync_Successful_ReturnsTrue()
    {
        // Arrange
        var responseJson = """
        [
            {
                "channel": "/meta/subscribe",
                "clientId": "client-abc",
                "subscription": "/data/Order__ChangeEvent",
                "successful": true
            }
        ]
        """;

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            var body = await req.Content!.ReadAsStringAsync(ct);
            body.Should().Contain("/meta/subscribe");
            body.Should().Contain("client-abc");
            body.Should().Contain("/data/Order__ChangeEvent");
            body.Should().Contain("42100");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        });

        using var client = new SalesforceBayeuxClient(new HttpClient(handler));

        // Act
        var result = await client.SubscribeAsync(
            InstanceUrl,
            CometdVersion,
            AccessToken,
            "client-abc",
            "/data/Order__ChangeEvent",
            42100);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task SubscribeAsync_SessionExpiredError_ThrowsWithShouldRehandshakeTrue()
    {
        // Arrange
        var responseJson = """
        [
            {
                "channel": "/meta/subscribe",
                "successful": false,
                "error": "403::Unknown client"
            }
        ]
        """;

        var handler = new MockHttpMessageHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        }));

        using var client = new SalesforceBayeuxClient(new HttpClient(handler));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<SalesforceBayeuxException>(() => client.SubscribeAsync(
            InstanceUrl,
            CometdVersion,
            AccessToken,
            "client-abc",
            "/data/Order__ChangeEvent",
            -1));

        ex.ShouldRehandshake.Should().BeTrue();
        ex.ErrorCode.Should().Be("403::Unknown client");
    }

    [Fact]
    public async Task ConnectAsync_ReceivesEvents_ParsesCorrectly()
    {
        // Arrange
        var responseJson = """
        [
            {
                "channel": "/meta/connect",
                "successful": true
            },
            {
                "channel": "/data/FF_PickOrder__ChangeEvent",
                "data": {
                    "schema": "schema_id_123",
                    "payload": {
                        "ChangeEventHeader": {
                            "entityName": "FF_PickOrder__c",
                            "transactionKey": "tx-key-789"
                        },
                        "OrderNumber__c": "ORD-001"
                    },
                    "event": {
                        "replayId": 55100,
                        "createdDate": "2026-09-17T12:30:00.000Z"
                    }
                }
            }
        ]
        """;

        var handler = new MockHttpMessageHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        }));

        using var client = new SalesforceBayeuxClient(new HttpClient(handler));

        // Act
        var events = await client.ConnectAsync(InstanceUrl, CometdVersion, AccessToken, "client-abc");

        // Assert
        events.Should().HaveCount(1);
        var evt = events[0];
        evt.Channel.Should().Be("/data/FF_PickOrder__ChangeEvent");
        evt.ReplayId.Should().Be(55100);
        evt.Schema.Should().Be("schema_id_123");
        evt.EventId.Should().Be("tx-key-789");
        evt.GetRawJson().Should().Contain("ORD-001");
        evt.CreatedDate.Should().Be(DateTimeOffset.Parse("2026-09-17T12:30:00.000Z", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ConnectAsync_ConnectFailed_ThrowsSalesforceBayeuxException()
    {
        // Arrange
        var responseJson = """
        [
            {
                "channel": "/meta/connect",
                "successful": false,
                "error": "403::Unknown client"
            }
        ]
        """;

        var handler = new MockHttpMessageHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        }));

        using var client = new SalesforceBayeuxClient(new HttpClient(handler));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<SalesforceBayeuxException>(() =>
            client.ConnectAsync(InstanceUrl, CometdVersion, AccessToken, "client-abc"));

        ex.ShouldRehandshake.Should().BeTrue();
    }

    [Fact]
    public async Task DisconnectAsync_SendsDisconnectMessageWithoutError()
    {
        // Arrange
        var called = false;
        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            called = true;
            var body = await req.Content!.ReadAsStringAsync(ct);
            body.Should().Contain("/meta/disconnect");
            body.Should().Contain("client-to-disconnect");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            };
        });

        using var client = new SalesforceBayeuxClient(new HttpClient(handler));

        // Act
        await client.DisconnectAsync(InstanceUrl, CometdVersion, AccessToken, "client-to-disconnect");

        // Assert
        called.Should().BeTrue();
    }

    [Theory]
    [InlineData("", CometdVersion, AccessToken)]
    [InlineData("  ", CometdVersion, AccessToken)]
    [InlineData(InstanceUrl, "", AccessToken)]
    [InlineData(InstanceUrl, CometdVersion, "")]
    public async Task HandshakeAsync_InvalidArguments_ThrowsArgumentException(string url, string ver, string token)
    {
        // Arrange
        using var client = new SalesforceBayeuxClient(new HttpClient(new MockHttpMessageHandler((req, ct) => Task.FromResult(new HttpResponseMessage()))));

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => client.HandshakeAsync(url, ver, token));
    }

    [Fact]
    public async Task GatewayHandler_RewritesTheBearerTokenToTheGatewayHeader_OnEveryCometdCall()
    {
        // N1: the documented recipe (docs/wiki/integrations/salesforce-streaming.md, "Behind an API gateway").
        var seen = await RunCometdCallsAsync(new GatewayApiKeyHandler("apiKey"));

        seen.Should().HaveCount(3);
        seen.Should().OnlyContain(h => h.Authorization == null && h.ApiKey == AccessToken);
    }

    [Fact]
    public async Task WithoutTheGatewayHandler_EveryCometdCallStillSendsBearer()
    {
        // N2: the default is untouched.
        var seen = await RunCometdCallsAsync(null);

        seen.Should().HaveCount(3);
        seen.Should().OnlyContain(h => h.Authorization == "Bearer " + AccessToken && h.ApiKey == null);
    }

    [Fact]
    public async Task GatewayHandler_WithARequestWithoutAuthorization_PassesItThroughUnchanged()
    {
        // N3: nothing to rewrite, nothing thrown.
        using var capture = new MockHttpMessageHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var invoker = new HttpMessageInvoker(new GatewayApiKeyHandler("apiKey") { InnerHandler = capture, });
        using var request = new HttpRequestMessage(HttpMethod.Post, InstanceUrl);

        using var response = await invoker.SendAsync(request, CancellationToken.None);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        request.Headers.Contains("apiKey").Should().BeFalse();
    }

    private static async Task<List<(string? Authorization, string? ApiKey)>> RunCometdCallsAsync(DelegatingHandler? gateway)
    {
        var seen = new List<(string? Authorization, string? ApiKey)>();
        var capture = new MockHttpMessageHandler(async (req, ct) =>
        {
            seen.Add((req.Headers.Authorization?.ToString(), req.Headers.TryGetValues("apiKey", out var v) ? v.Single() : null));
            var body = await req.Content!.ReadAsStringAsync(ct);
            var channel = "/meta/connect";
            foreach (var candidate in new[] { "/meta/handshake", "/meta/subscribe", })
            {
                if (body.Contains(candidate, StringComparison.Ordinal))
                {
                    channel = candidate;
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""[{"channel":"{{channel}}","clientId":"client-abc","successful":true}]""", Encoding.UTF8, "application/json"),
            };
        });
        HttpMessageHandler pipeline = capture;
        if (gateway is not null)
        {
            gateway.InnerHandler = capture;
            pipeline = gateway;
        }

        using var client = new SalesforceBayeuxClient(new HttpClient(pipeline));
        await client.HandshakeAsync(InstanceUrl, CometdVersion, AccessToken);
        await client.SubscribeAsync(InstanceUrl, CometdVersion, AccessToken, "client-abc", "/data/Order__ChangeEvent", 1);
        await client.ConnectAsync(InstanceUrl, CometdVersion, AccessToken, "client-abc");
        return seen;
    }

    // Same code as the recipe in the docs.
    private sealed class GatewayApiKeyHandler : DelegatingHandler
    {
        private readonly string _headerName;

        public GatewayApiKeyHandler(string headerName) => _headerName = headerName;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization is { } auth)
            {
                request.Headers.Authorization = null;
                request.Headers.TryAddWithoutValidation(_headerName, auth.Parameter);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsyncFunc)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => sendAsyncFunc(request, cancellationToken);
    }
}
