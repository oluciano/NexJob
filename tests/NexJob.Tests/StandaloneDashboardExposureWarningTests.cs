using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob.Dashboard.Standalone;
using Xunit;

namespace NexJob.Tests;

public sealed class StandaloneDashboardExposureWarningTests
{
    [Fact]
    public async Task StartAsync_LocalhostOnlyFalse_LogsExposureWarning()
    {
        // N1 (Positive): listening on all interfaces without authorization must warn once.
        var sink = new LevelCapturingLoggerProvider();
        var port = GetFreeTcpPort();

        await RunAsync(sink, port, "/dashboard", localhostOnly: false);

        var warnings = sink.Entries.Where(e => e.Level == LogLevel.Warning && e.Message.Contains("authorization", StringComparison.Ordinal)).ToList();
        warnings.Should().ContainSingle();
        warnings[0].Message.Should().Contain("LocalhostOnly = true");
    }

    [Fact]
    public async Task StartAsync_LocalhostOnlyTrue_LogsNoExposureWarning()
    {
        // N2 (Negative): loopback-only is safe, so nothing must be warned.
        var sink = new LevelCapturingLoggerProvider();
        var port = GetFreeTcpPort();

        await RunAsync(sink, port, "/dashboard", localhostOnly: true);

        sink.Entries.Should().NotContain(e => e.Level == LogLevel.Warning && e.Message.Contains("authorization", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_CustomPortAndPath_AppearInWarning()
    {
        // N3 (Invalid Input / boundary): the message must carry the real port and path.
        var sink = new LevelCapturingLoggerProvider();
        var port = GetFreeTcpPort();

        await RunAsync(sink, port, "/ops/jobs", localhostOnly: false);

        sink.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning
            && e.Message.Contains($":{port}", StringComparison.Ordinal)
            && e.Message.Contains("/ops/jobs", StringComparison.Ordinal));
    }

    private static async Task RunAsync(ILoggerProvider sink, int port, string path, bool localhostOnly)
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.AddProvider(sink))
            .ConfigureServices(services =>
            {
                services.AddNexJob();
                services.AddNexJobStandaloneDashboard(options =>
                {
                    options.Port = port;
                    options.Path = path;
                    options.LocalhostOnly = localhostOnly;
                });
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

    private sealed class LevelCapturingLoggerProvider : ILoggerProvider
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

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<(LogLevel Level, string Message)> entries) : ILogger
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
