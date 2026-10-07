using Microsoft.Extensions.Logging;

namespace NexJob.ReliabilityTests;

/// <summary>
/// Completes when the dispatcher logs that every queue is paused, which proves a polling cycle
/// ran after the pause was stored. The fetch loop is sequential, so a cycle that started before
/// the pause has finished by then and cannot fetch a job enqueued afterwards.
/// </summary>
public sealed class PauseObservedSignal : ILoggerProvider
{
    private const string Marker = "all queues are paused";

    private readonly TaskCompletionSource _observed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Waits until the dispatcher has logged a cycle that found every queue paused.</summary>
    /// <param name="timeout">How long to wait before failing.</param>
    /// <returns>A task that completes once the pause has been observed.</returns>
    public async Task WaitAsync(TimeSpan timeout)
    {
        var finished = await Task.WhenAny(_observed.Task, Task.Delay(timeout));
        if (finished != _observed.Task)
        {
            throw new TimeoutException("The dispatcher did not log a polling cycle with every queue paused.");
        }
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new SignalLogger(_observed);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class SignalLogger : ILogger
    {
        private readonly TaskCompletionSource _observed;

        public SignalLogger(TaskCompletionSource observed) => _observed = observed;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).Contains(Marker, StringComparison.Ordinal))
            {
                _observed.TrySetResult();
            }
        }
    }
}
