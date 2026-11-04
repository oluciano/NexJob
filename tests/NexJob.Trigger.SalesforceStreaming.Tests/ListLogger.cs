using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace NexJob.Trigger.SalesforceStreaming.Tests;

/// <summary>Collects what the code under test logs, so a test can assert on level, message and exception.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => _entries.ToArray();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _entries.Enqueue(new LogEntry(logLevel, exception, formatter(state, exception)));
    }
}
