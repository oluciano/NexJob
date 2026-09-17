using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace NexJob.Trigger.Salesforce.Tests;

public sealed class SalesforceTokenProviderTests
{
    private readonly SalesforceTriggerOptions _options = new()
    {
        Topic = "/data/ChangeEvents",
        ClientId = "client-123",
        ClientSecret = "secret-abc",
        AuthEndpoint = "https://login.salesforce.com/services/oauth2/token",
    };

    [Fact]
    public async Task GetTokenAsync_SuccessfulResponse_ParsesAndReturnsToken()
    {
        // Arrange
        var jsonResponse = """
        {
            "access_token": "mock-access-token-999",
            "instance_url": "https://na1.salesforce.com",
            "id": "https://login.salesforce.com/id/00D50000000IZ3pEAG/00550000001dummy",
            "token_type": "Bearer",
            "expires_in": 3600
        }
        """;

        var handlerMock = CreateMockHandler(HttpStatusCode.OK, jsonResponse);
        using var httpClient = new HttpClient(handlerMock.Object);
        using var provider = new SalesforceTokenProvider(
            Options.Create(_options),
            httpClient,
            NullLogger<SalesforceTokenProvider>.Instance);

        // Act
        var token = await provider.GetTokenAsync();

        // Assert
        token.Should().NotBeNull();
        token.AccessToken.Should().Be("mock-access-token-999");
        token.InstanceUrl.Should().Be("https://na1.salesforce.com");
        token.TenantId.Should().Be("00D50000000IZ3pEAG");
        token.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(50));
    }

    [Fact]
    public async Task GetTokenAsync_ExplicitTenantIdConfigured_UsesConfiguredTenantId()
    {
        // Arrange
        _options.TenantId = "00D_CUSTOM_TENANT";
        var jsonResponse = """
        {
            "access_token": "tok-1",
            "instance_url": "https://custom.salesforce.com",
            "id": "https://login.salesforce.com/id/00D_IGNORED/005_IGNORED"
        }
        """;

        var handlerMock = CreateMockHandler(HttpStatusCode.OK, jsonResponse);
        using var httpClient = new HttpClient(handlerMock.Object);
        using var provider = new SalesforceTokenProvider(
            Options.Create(_options),
            httpClient,
            NullLogger<SalesforceTokenProvider>.Instance);

        // Act
        var token = await provider.GetTokenAsync();

        // Assert
        token.TenantId.Should().Be("00D_CUSTOM_TENANT");
    }

    [Fact]
    public async Task GetTokenAsync_SubsequentCall_ReturnsCachedTokenWithoutHttpCall()
    {
        // Arrange
        var jsonResponse = """
        {
            "access_token": "cached-token",
            "instance_url": "https://na1.salesforce.com",
            "expires_in": 3600
        }
        """;

        var handlerMock = CreateMockHandler(HttpStatusCode.OK, jsonResponse);
        using var httpClient = new HttpClient(handlerMock.Object);
        using var provider = new SalesforceTokenProvider(
            Options.Create(_options),
            httpClient,
            NullLogger<SalesforceTokenProvider>.Instance);

        // Act
        var first = await provider.GetTokenAsync();
        var second = await provider.GetTokenAsync();

        // Assert
        second.Should().BeSameAs(first);
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetTokenAsync_ForceRefreshTrue_RequestsNewTokenFromEndpoint()
    {
        // Arrange
        var jsonResponse1 = """{"access_token": "token-1", "expires_in": 3600}""";
        var jsonResponse2 = """{"access_token": "token-2", "expires_in": 3600}""";

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .SetupSequence<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(jsonResponse1, Encoding.UTF8, "application/json") })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(jsonResponse2, Encoding.UTF8, "application/json") });

        using var httpClient = new HttpClient(handlerMock.Object);
        using var provider = new SalesforceTokenProvider(
            Options.Create(_options),
            httpClient,
            NullLogger<SalesforceTokenProvider>.Instance);

        // Act
        var first = await provider.GetTokenAsync();
        var second = await provider.GetTokenAsync(forceRefresh: true);

        // Assert
        first.AccessToken.Should().Be("token-1");
        second.AccessToken.Should().Be("token-2");
        second.Should().NotBeSameAs(first);
    }

    [Fact]
    public async Task GetTokenAsync_HttpError_ThrowsSalesforceAuthenticationException()
    {
        // Arrange
        var errorJson = """{"error": "invalid_client", "error_description": "invalid client credentials"}""";
        var handlerMock = CreateMockHandler(HttpStatusCode.BadRequest, errorJson);
        using var httpClient = new HttpClient(handlerMock.Object);
        using var provider = new SalesforceTokenProvider(
            Options.Create(_options),
            httpClient,
            NullLogger<SalesforceTokenProvider>.Instance);

        // Act
        Func<Task> act = async () => await provider.GetTokenAsync();

        // Assert
        var ex = await act.Should().ThrowAsync<SalesforceAuthenticationException>();
        ex.WithMessage("*invalid_client*");
    }

    [Fact]
    public async Task GetTokenAsync_EmptyResponseBody_ThrowsSalesforceAuthenticationException()
    {
        // Arrange
        var handlerMock = CreateMockHandler(HttpStatusCode.OK, "null");
        using var httpClient = new HttpClient(handlerMock.Object);
        using var provider = new SalesforceTokenProvider(
            Options.Create(_options),
            httpClient,
            NullLogger<SalesforceTokenProvider>.Instance);

        // Act
        Func<Task> act = async () => await provider.GetTokenAsync();

        // Assert
        await act.Should().ThrowAsync<SalesforceAuthenticationException>();
    }

    [Fact]
    public void Constructor_NullParameters_ThrowsArgumentNullException()
    {
        using var httpClient = new HttpClient();
        var options = Options.Create(_options);
        var logger = NullLogger<SalesforceTokenProvider>.Instance;

        var act1 = () => new SalesforceTokenProvider(null!, httpClient, logger);
        var act2 = () => new SalesforceTokenProvider(options, null!, logger);
        var act3 = () => new SalesforceTokenProvider(options, httpClient, null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
        act3.Should().Throw<ArgumentNullException>();
    }

    private static Mock<HttpMessageHandler> CreateMockHandler(HttpStatusCode statusCode, string content)
    {
        var mock = new Mock<HttpMessageHandler>();
        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });

        return mock;
    }
}
