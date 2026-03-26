using Microsoft.Extensions.Logging;

namespace NexJob.Internal;

/// <summary>
/// An <see cref="ILoggerProvider"/> that captures log entries emitted during job execution.
/// Register it with the scope's <see cref="ILoggerFactory"/> before executing the job, then
/// read <see cref="Entries"/> after execution to persist the captured output.
/// </summary>
/// <remarks>
/// Because <see cref="ILoggerFactory.AddProvider"/> affects the singleton factory, this provider
/// captures all log categories emitted during the job execution window — not only the job's own
/// logger. Entries are capped at <see cref="_maxLines"/> to bound memory usage.
/// </remarks>
internal sealed class JobCaptureLoggerProvider : ILoggerProvider
{
    private readonly List<JobExecutionLog> _entries = new();
    private readonly int _maxLines;

    /// <summary>Initialises the provider with the given line cap.</summary>
    public JobCaptureLoggerProvider(int maxLines) => _maxLines = maxLines;

    /// <summary>All captured log entries in emission order.</summary>
    public IReadOnlyList<JobExecutionLog> Entries => _entries;

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this);

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    // ─── inner logger ────────────────────────────────────────────────────────

    private sealed class CaptureLogger : ILogger
    {
        private readonly JobCaptureLoggerProvider _provider;

        public CaptureLogger(JobCaptureLoggerProvider provider)
        {
            _provider = provider;
        }

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        /// <inheritdoc/>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (_provider._entries.Count >= _provider._maxLines)
            {
                return;
            }

            var message = formatter(state, exception);
            if (exception != null)
            {
                message += $"\n{exception}";
            }

            _provider._entries.Add(new JobExecutionLog
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = logLevel.ToString(),
                Message = message,
            });
        }
    }
}
