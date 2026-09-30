using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Behavior changed in v5.6: batch acknowledgment now releases continuations (issue #257), so the startup warning
/// added in the documentation step is gone. These tests guard that no such warning is logged any more.
/// </summary>
public sealed class BatchAckContinuationWarningTests
{
    /// <summary>N1 (Positive): enabling batch acknowledgment no longer logs a continuation warning.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task BatchAckEnabled_NoLongerWarnsAboutContinuations()
    {
        var warnings = await RunHostAsync(batchAck: true);

        warnings.Should().NotContain(m => m.Contains("EnableBatchAcknowledgment", StringComparison.Ordinal));
    }

    /// <summary>N2 (Negative): with the default settings no such warning is logged.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task BatchAckDisabled_LogsNoWarning()
    {
        var warnings = await RunHostAsync(batchAck: false);

        warnings.Should().NotContain(m => m.Contains("EnableBatchAcknowledgment", StringComparison.Ordinal));
    }

    /// <summary>N3 (Boundary): no warning appears across several polling cycles either.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task BatchAckEnabled_NoWarningAcrossPollingCycles()
    {
        var warnings = await RunHostAsync(batchAck: true, runFor: TimeSpan.FromMilliseconds(400));

        warnings.Count(m => m.Contains("EnableBatchAcknowledgment", StringComparison.Ordinal)).Should().Be(0);
    }

    private static async Task<List<string>> RunHostAsync(bool batchAck, TimeSpan? runFor = null)
    {
        var sink = new WarningSink();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.AddProvider(sink))
            .ConfigureServices(services => services.AddNexJob(opt =>
            {
                opt.PollingInterval = TimeSpan.FromMilliseconds(20);
                opt.EnableBatchAcknowledgment = batchAck;
            }))
            .Build();

        await host.StartAsync();
        await Task.Delay(runFor ?? TimeSpan.FromMilliseconds(150));
        await host.StopAsync();
        return sink.Warnings;
    }

    private sealed class WarningSink : ILoggerProvider
    {
        private readonly List<string> _warnings = [];

        public List<string> Warnings
        {
            get
            {
                lock (_warnings)
                {
                    return [.. _warnings];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new SinkLogger(_warnings);

        public void Dispose()
        {
        }

        private sealed class SinkLogger(List<string> warnings) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                {
                    lock (warnings)
                    {
                        warnings.Add(formatter(state, exception));
                    }
                }
            }
        }
    }
}
