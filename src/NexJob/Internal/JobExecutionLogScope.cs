using Microsoft.Extensions.Logging;

namespace NexJob.Internal;

/// <summary>
/// Holds the log capture list for a single job execution and associates it with the
/// current async execution context via <see cref="AsyncLocal{T}"/>.
/// </summary>
/// <remarks>
/// Create one instance per job execution using a <c>using</c> statement.
/// On construction, the scope installs itself as the current value of the
/// <see cref="AsyncLocal{T}"/> so that any logger running in the same async context
/// (or child tasks spawned after construction) will route entries here.
/// On <see cref="Dispose"/>, the previous scope value is restored — in practice always
/// <see langword="null"/> because each job runs in its own isolated task context.
/// </remarks>
internal sealed class JobExecutionLogScope : IDisposable
{
    private static readonly AsyncLocal<JobExecutionLogScope?> _current = new();

    private readonly List<JobExecutionLog> _entries = new();
    private readonly int _maxLines;
    private readonly JobExecutionLogScope? _previous;

    /// <summary>
    /// Initialises a new scope with the given line cap and installs it as the current async-local scope.
    /// </summary>
    /// <param name="maxLines">Maximum number of log lines to retain.</param>
    public JobExecutionLogScope(int maxLines)
    {
        _maxLines = maxLines;
        _previous = _current.Value;
        _current.Value = this;
    }

    /// <summary>The active <see cref="JobExecutionLogScope"/> for the current async context, or <see langword="null"/>.</summary>
    public static JobExecutionLogScope? Current => _current.Value;

    /// <summary>All log entries captured during this execution scope, in emission order.</summary>
    public IReadOnlyList<JobExecutionLog> Entries => _entries;

    /// <summary>
    /// Appends a log entry to this scope's capture list, subject to the configured line cap.
    /// </summary>
    /// <param name="level">The log level of the entry.</param>
    /// <param name="message">The formatted log message.</param>
    public void Capture(LogLevel level, string message)
    {
        if (_entries.Count < _maxLines)
        {
            _entries.Add(new JobExecutionLog
            {
                Timestamp = DateTimeOffset.UtcNow,
                Level = level.ToString(),
                Message = message,
            });
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _current.Value = _previous;
    }
}
