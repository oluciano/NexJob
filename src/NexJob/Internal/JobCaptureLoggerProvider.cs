using Microsoft.Extensions.Logging;

namespace NexJob.Internal;

/// <summary>
/// A singleton <see cref="ILoggerProvider"/> registered once at startup that routes log
/// entries to whichever <see cref="JobExecutionLogScope"/> is active in the current async
/// execution context.
/// </summary>
/// <remarks>
/// Because it uses <see cref="AsyncLocal{T}"/> via <see cref="JobExecutionLogScope.Current"/>,
/// concurrent jobs running in separate isolated task contexts each see their own
/// isolated scope, preventing cross-job log bleed. Categories not running within a
/// <see cref="JobExecutionLogScope"/> (e.g. ASP.NET request logs) are silently ignored.
/// </remarks>
internal sealed class JobCaptureLoggerProvider : ILoggerProvider
{
    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new CaptureLogger();

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    // ─── inner logger ────────────────────────────────────────────────────────

    private sealed class CaptureLogger : ILogger
    {
        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) =>
            logLevel != LogLevel.None && JobExecutionLogScope.Current != null;

        /// <inheritdoc/>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var scope = JobExecutionLogScope.Current;
            if (scope is null)
            {
                return;
            }

            var message = formatter(state, exception);
            if (exception != null)
            {
                message += $"\n{exception}";
            }

            scope.Capture(logLevel, message);
        }
    }
}
