using Microsoft.Extensions.Logging;

namespace NexJob.Trigger.Salesforce.Tests;

/// <summary>Collects what the code under test logs, so a test can assert on level, message and exception.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly object _gate = new();
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_gate)
        {
            _entries.Add(new LogEntry(logLevel, exception, formatter(state, exception)));
        }
    }
}
