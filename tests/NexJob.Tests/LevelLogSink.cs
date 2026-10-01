using Microsoft.Extensions.Logging;

namespace NexJob.Tests;

/// <summary>A logger provider that keeps the level and the formatted message of every entry.</summary>
internal sealed class LevelLogSink : ILoggerProvider
{
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    /// <summary>All captured entries.</summary>
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

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new SinkLogger(_entries);

    /// <inheritdoc/>
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
