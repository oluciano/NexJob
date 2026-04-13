using Microsoft.Extensions.Logging;

namespace NexJob.Trigger.AwsSqs.Tests;

/// <summary>
/// Minimal logger implementation for testing.
/// </summary>
/// <typeparam name="T">The category type for the logger.</typeparam>
internal sealed class MockLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(
        TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // No-op — logs can be inspected if needed
    }
}
