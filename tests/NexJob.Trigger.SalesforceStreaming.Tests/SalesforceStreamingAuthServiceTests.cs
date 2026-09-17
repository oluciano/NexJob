using System.Net;
using System.Text;
using FluentAssertions;
using Xunit;

namespace NexJob.Trigger.SalesforceStreaming.Tests;

public sealed class SalesforceStreamingAuthServiceTests
{
    [Fact]
    public async Task GetTokenAsync_SessionId_ReturnsDirectTokenWithoutHttpCall()
    {
        // Arrange
        var handler = new MockHttpMessageHandler((req, ct) => throw new InvalidOperationException("Should not be called"));
        using var httpClient = new HttpClient(handler);
        using var authService = new SalesforceStreamingAuthService(httpClient);

        var options = new SalesforceStreamingAuthOptions
        {
            AuthType = SalesforceStreamingAuthType.SessionId,
            InstanceUrl = "https://myinstance.salesforce.com",
            AccessToken = "00DtestSessionId",
        };

        // Act
        var result = await authService.GetTokenAsync(options);

        // Assert
        result.AccessToken.Should().Be("00DtestSessionId");
        result.InstanceUrl.Should().Be("https://myinstance.salesforce.com");
    }

    [Fact]
    public async Task GetTokenAsync_OAuth2UsernamePassword_Success_ParsesAndCachesToken()
    {
        // Arrange
        var callCount = 0;
        var responseJson = """
        {
            "access_token": "00D5e000000XyzToken",
            "instance_url": "https://company.my.salesforce.com",
            "token_type": "Bearer"
        }
        """;

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            callCount++;
            var body = await req.Content!.ReadAsStringAsync(ct);
            body.Should().Contain("grant_type=password");
            body.Should().Contain("client_id=my-client-id");
            body.Should().Contain("client_secret=my-client-secret");
            body.Should().Contain("username=user%40test.com");
            body.Should().Contain("password=mypasswordmytoken");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        });

        using var httpClient = new HttpClient(handler);
        using var authService = new SalesforceStreamingAuthService(httpClient);

        var options = new SalesforceStreamingAuthOptions
        {
            AuthType = SalesforceStreamingAuthType.OAuth2UsernamePassword,
            AuthEndpoint = "https://login.salesforce.com/services/oauth2/token",
            ClientId = "my-client-id",
            ClientSecret = "my-client-secret",
            Username = "user@test.com",
            Password = "mypassword",
            SecurityToken = "mytoken",
        };

        // Act - first call
        var result1 = await authService.GetTokenAsync(options);

        // Act - second call should use cached token
        var result2 = await authService.GetTokenAsync(options);

        // Assert
        result1.AccessToken.Should().Be("00D5e000000XyzToken");
        result1.InstanceUrl.Should().Be("https://company.my.salesforce.com");
        result2.Should().BeSameAs(result1);
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task GetTokenAsync_OAuth2ClientCredentials_Success()
    {
        // Arrange
        var responseJson = """
        {
            "access_token": "00DclientCredsToken",
            "instance_url": "https://creds.my.salesforce.com"
        }
        """;

        var handler = new MockHttpMessageHandler(async (req, ct) =>
        {
            var body = await req.Content!.ReadAsStringAsync(ct);
            body.Should().Contain("grant_type=client_credentials");
            body.Should().Contain("client_id=client1");
            body.Should().Contain("client_secret=secret1");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        });

        using var httpClient = new HttpClient(handler);
        using var authService = new SalesforceStreamingAuthService(httpClient);

        var options = new SalesforceStreamingAuthOptions
        {
            AuthType = SalesforceStreamingAuthType.OAuth2ClientCredentials,
            AuthEndpoint = "https://login.salesforce.com/services/oauth2/token",
            ClientId = "client1",
            ClientSecret = "secret1",
        };

        // Act
        var result = await authService.GetTokenAsync(options);

        // Assert
        result.AccessToken.Should().Be("00DclientCredsToken");
        result.InstanceUrl.Should().Be("https://creds.my.salesforce.com");
    }

    [Fact]
    public async Task InvalidateToken_CausesRefetchOnNextCall()
    {
        // Arrange
        var callCount = 0;
        var responseJson = """
        {
            "access_token": "token1",
            "instance_url": "https://test.salesforce.com"
        }
        """;

        var handler = new MockHttpMessageHandler((req, ct) =>
        {
            callCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
        });

        using var httpClient = new HttpClient(handler);
        using var authService = new SalesforceStreamingAuthService(httpClient);

        var options = new SalesforceStreamingAuthOptions
        {
            AuthType = SalesforceStreamingAuthType.OAuth2ClientCredentials,
            ClientId = "c",
            ClientSecret = "s",
        };

        // Act
        await authService.GetTokenAsync(options);
        authService.InvalidateToken();
        await authService.GetTokenAsync(options);

        // Assert
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task GetTokenAsync_HttpError_ThrowsSalesforceAuthenticationException()
    {
        // Arrange
        var errorJson = """
        {
            "error": "invalid_grant",
            "error_description": "authentication failure"
        }
        """;

        var handler = new MockHttpMessageHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(errorJson, Encoding.UTF8, "application/json"),
        }));

        using var httpClient = new HttpClient(handler);
        using var authService = new SalesforceStreamingAuthService(httpClient);

        var options = new SalesforceStreamingAuthOptions
        {
            AuthType = SalesforceStreamingAuthType.OAuth2UsernamePassword,
            ClientId = "c",
            ClientSecret = "s",
            Username = "u",
            Password = "p",
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<SalesforceAuthenticationException>(() => authService.GetTokenAsync(options));
        ex.Message.Should().Contain("authentication failure");
    }

    [Fact]
    public async Task GetTokenAsync_MissingTokenInResponse_ThrowsSalesforceAuthenticationException()
    {
        // Arrange
        var jsonWithoutToken = """
        {
            "instance_url": "https://myinstance.salesforce.com"
        }
        """;

        var handler = new MockHttpMessageHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonWithoutToken, Encoding.UTF8, "application/json"),
        }));

        using var httpClient = new HttpClient(handler);
        using var authService = new SalesforceStreamingAuthService(httpClient);

        var options = new SalesforceStreamingAuthOptions
        {
            AuthType = SalesforceStreamingAuthType.OAuth2ClientCredentials,
            ClientId = "c",
            ClientSecret = "s",
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<SalesforceAuthenticationException>(() => authService.GetTokenAsync(options));
        ex.Message.Should().Contain("access_token");
    }

    [Fact]
    public async Task GetTokenAsync_MissingInstanceUrlInResponse_ThrowsSalesforceAuthenticationException()
    {
        // Arrange
        var jsonWithoutInstanceUrl = """
        {
            "access_token": "valid_token"
        }
        """;

        var handler = new MockHttpMessageHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonWithoutInstanceUrl, Encoding.UTF8, "application/json"),
        }));

        using var httpClient = new HttpClient(handler);
        using var authService = new SalesforceStreamingAuthService(httpClient);

        var options = new SalesforceStreamingAuthOptions
        {
            AuthType = SalesforceStreamingAuthType.OAuth2ClientCredentials,
            ClientId = "c",
            ClientSecret = "s",
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<SalesforceAuthenticationException>(() => authService.GetTokenAsync(options));
        ex.Message.Should().Contain("instance_url");
    }

    [Fact]
    public async Task GetTokenAsync_HttpErrorWithNonJson_FallsBackToRawBody()
    {
        // Arrange
        var handler = new MockHttpMessageHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>502 Bad Gateway</html>", Encoding.UTF8, "text/html"),
        }));

        using var httpClient = new HttpClient(handler);
        using var authService = new SalesforceStreamingAuthService(httpClient);

        var options = new SalesforceStreamingAuthOptions
        {
            AuthType = SalesforceStreamingAuthType.OAuth2ClientCredentials,
            ClientId = "c",
            ClientSecret = "s",
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<SalesforceAuthenticationException>(() => authService.GetTokenAsync(options));
        ex.Message.Should().Contain("502 Bad Gateway");
    }

    [Fact]
    public async Task GetTokenAsync_HttpErrorWithErrorField_UsesErrorField()
    {
        // Arrange
        var jsonWithError = """
        {
            "error": "invalid_grant"
        }
        """;

        var handler = new MockHttpMessageHandler((req, ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(jsonWithError, Encoding.UTF8, "application/json"),
        }));

        using var httpClient = new HttpClient(handler);
        using var authService = new SalesforceStreamingAuthService(httpClient);

        var options = new SalesforceStreamingAuthOptions
        {
            AuthType = SalesforceStreamingAuthType.OAuth2ClientCredentials,
            ClientId = "c",
            ClientSecret = "s",
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<SalesforceAuthenticationException>(() => authService.GetTokenAsync(options));
        ex.Message.Should().Contain("invalid_grant");
    }

    [Fact]
    public async Task GetTokenAsync_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        using var httpClient = new HttpClient(new MockHttpMessageHandler((req, ct) => Task.FromResult(new HttpResponseMessage())));
        using var authService = new SalesforceStreamingAuthService(httpClient);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => authService.GetTokenAsync(null!));
    }

    private sealed class MockHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsyncFunc)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => sendAsyncFunc(request, cancellationToken);
    }
}
