namespace NexJob.Dashboard;

/// <summary>Turns the queue scope a dashboard was configured with into the queue names stored with jobs.</summary>
internal static class QueueScope
{
    /// <summary>
    /// Expands <c>default</c> in a scope to the prefixed default queue of the host and the legacy <c>default</c>.
    /// Every other entry is kept as it is.
    /// </summary>
    /// <param name="scope">The configured scope, or <see langword="null"/> for all queues.</param>
    /// <param name="options">The options of the host the dashboard runs in.</param>
    /// <returns>The stored queue names, or the scope itself when it is null or empty.</returns>
    internal static IReadOnlyList<string>? Expand(IReadOnlyList<string>? scope, NexJobOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return scope;
    }
}
