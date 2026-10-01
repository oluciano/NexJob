using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Configuration;
using NexJob.Dashboard.Standalone;
using Xunit;

namespace NexJob.Tests;

public sealed class StandaloneDashboardAuthorizationTests
{
    [Fact]
    public async Task Handler_Denies_ReturnsUnauthorized()
    {
        // N1 (Positive): a registered handler is enforced in standalone mode.
        var status = await GetStatusAsync(s => s.AddSingleton<IDashboardAuthorizationHandler, FixedHandler>(_ => new FixedHandler(false)));

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Handler_Allows_ReturnsOk()
    {
        var status = await GetStatusAsync(s => s.AddSingleton<IDashboardAuthorizationHandler, FixedHandler>(_ => new FixedHandler(true)));

        status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ScopedHandler_IsResolvedPerRequest()
    {
        // N1: any handler lifetime works, a scoped one must not throw or be skipped.
        var status = await GetStatusAsync(s => s.AddScoped<IDashboardAuthorizationHandler, ScopedDenyHandler>());

        status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Handler_ReceivesTheRequest_SoItCanAuthenticateItself()
    {
        // N1: standalone has no authentication middleware, so the handler decides from the request itself.
        var denied = await GetStatusAsync(s => s.AddSingleton<IDashboardAuthorizationHandler, HeaderKeyHandler>());
        var allowed = await GetStatusAsync(s => s.AddSingleton<IDashboardAuthorizationHandler, HeaderKeyHandler>(), "secret");

        denied.Should().Be(HttpStatusCode.Unauthorized);
        allowed.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Handler_Throws_FailsClosed()
    {
        // N2 (Negative): a throwing handler must never grant access.
        var status = await GetStatusAsync(s => s.AddSingleton<IDashboardAuthorizationHandler, ThrowingHandler>());

        status.Should().NotBe(HttpStatusCode.OK);
    }

    [Fact]
    public void Options_Default_IsLoopbackOnly()
    {
        // N2: secure by default, both the standalone options and the core settings class.
        new StandaloneDashboardOptions().LocalhostOnly.Should().BeTrue();
        new DashboardSettings().LocalhostOnly.Should().BeTrue();
    }

    [Fact]
    public async Task DefaultOptions_ListenOnLocalhost_AndLogNoExposureWarning()
    {
        // N2: nothing configured means loopback only, with no warning.
        var sink = new LevelSink();
        var port = GetFreeTcpPort();

        await RunAsync(sink, services => services.AddNexJobStandaloneDashboard(o => o.Port = port));

        sink.Entries.Should().Contain(e => e.Message.Contains($"http://localhost:{port}", StringComparison.Ordinal));
        sink.Entries.Should().NotContain(e => e.Level == LogLevel.Warning && e.Message.Contains("reachable from the network", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Exposed_WithHandler_DoesNotWarn()
    {
        // N3: explicit opt-in to all interfaces plus a handler is the protected setup, so no warning.
        var sink = new LevelSink();
        var port = GetFreeTcpPort();

        await RunAsync(sink, services =>
        {
            services.AddSingleton<IDashboardAuthorizationHandler>(new FixedHandler(true));
            services.AddNexJobStandaloneDashboard(o =>
            {
                o.Port = port;
                o.LocalhostOnly = false;
            });
        });

        sink.Entries.Should().NotContain(e => e.Level == LogLevel.Warning && e.Message.Contains("reachable from the network", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Exposed_WithoutHandler_StillWorksAndWarns()
    {
        // N3: opting in without a handler keeps working and warns.
        var sink = new LevelSink();
        var port = GetFreeTcpPort();

        await RunAsync(sink, services => services.AddNexJobStandaloneDashboard(o =>
        {
            o.Port = port;
            o.LocalhostOnly = false;
        }));

        sink.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("reachable from the network", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BasicAuthHandler_NoCredentials_ReturnsUnauthorizedWithChallenge()
    {
        // N1: the handler can ask the browser for credentials; the dashboard keeps the header it set.
        using var response = await SendBasicAsync(null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().ContainSingle(h => h.Scheme == "Basic");
    }

    [Fact]
    public async Task BasicAuthHandler_RightCredentials_ReturnsOk()
    {
        using var response = await SendBasicAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes("ops:s3cret")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("ops:wrong")]
    [InlineData("")]
    [InlineData(":")]
    public async Task BasicAuthHandler_WrongCredentials_ReturnsUnauthorized(string credentials)
    {
        // N2 and N3: wrong, empty and blank credentials are all denied.
        using var response = await SendBasicAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task BasicAuthHandler_MalformedBase64_ReturnsUnauthorizedNotServerError()
    {
        // N3 (Invalid Input): garbage in the Authorization header is a denial, never a 500.
        using var response = await SendBasicAsync("%%%not-base64%%%");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<HttpResponseMessage> SendBasicAsync(string? base64)
    {
        var port = GetFreeTcpPort();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?> { ["Dashboard:Credentials"] = "ops:s3cret", }))
            .ConfigureServices(services =>
            {
                services.AddNexJob();
                services.AddSingleton<IDashboardAuthorizationHandler, BasicAuthDashboardHandler>();
                services.AddNexJobStandaloneDashboard(o => o.Port = port);
            })
            .Build();

        try
        {
            await host.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "/dashboard");
            if (base64 is not null)
            {
                request.Headers.TryAddWithoutValidation("Authorization", $"Basic {base64}");
            }

            return await client.SendAsync(request);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static async Task<HttpStatusCode> GetStatusAsync(Action<IServiceCollection> registerHandler, string? key = null)
    {
        var port = GetFreeTcpPort();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob();
                registerHandler(services);
                services.AddNexJobStandaloneDashboard(o => o.Port = port);
            })
            .Build();

        try
        {
            await host.StartAsync();
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(5) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "/dashboard");
            if (key is not null)
            {
                request.Headers.Add("X-Dashboard-Key", key);
            }

            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static async Task RunAsync(ILoggerProvider sink, Action<IServiceCollection> register)
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.AddProvider(sink))
            .ConfigureServices(services =>
            {
                services.AddNexJob();
                register(services);
            })
            .Build();

        try
        {
            await host.StartAsync();
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // Mirrors the example in docs/wiki/10-Dashboard.md, so the documented code is compiled and run.
    private sealed class BasicAuthDashboardHandler(IConfiguration config) : IDashboardAuthorizationHandler
    {
        public Task<bool> AuthorizeAsync(HttpContext context)
        {
            var expected = config["Dashboard:Credentials"]; // "user:password"
            if (!string.IsNullOrEmpty(expected)
                && TryReadCredentials(context, out var provided)
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided)))
            {
                return Task.FromResult(true);
            }

            // Ask the browser for credentials. The dashboard answers 401 when this returns false.
            context.Response.Headers.WWWAuthenticate = "Basic realm=\"NexJob\"";
            return Task.FromResult(false);
        }

        private static bool TryReadCredentials(HttpContext context, out string credentials)
        {
            credentials = string.Empty;
            var header = context.Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                credentials = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..].Trim()));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }

    private sealed class FixedHandler(bool allow) : IDashboardAuthorizationHandler
    {
        public Task<bool> AuthorizeAsync(HttpContext context) => Task.FromResult(allow);
    }

    private sealed class ScopedDenyHandler : IDashboardAuthorizationHandler
    {
        public Task<bool> AuthorizeAsync(HttpContext context) => Task.FromResult(false);
    }

    private sealed class HeaderKeyHandler : IDashboardAuthorizationHandler
    {
        public Task<bool> AuthorizeAsync(HttpContext context) =>
            Task.FromResult(string.Equals(context.Request.Headers["X-Dashboard-Key"], "secret", StringComparison.Ordinal));
    }

    private sealed class ThrowingHandler : IDashboardAuthorizationHandler
    {
        public Task<bool> AuthorizeAsync(HttpContext context) => throw new InvalidOperationException("handler failed");
    }

    private sealed class LevelSink : ILoggerProvider
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new SinkLogger(_entries);

        public void Dispose()
        {
        }

        private sealed class SinkLogger(List<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (entries)
                {
                    entries.Add((logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
