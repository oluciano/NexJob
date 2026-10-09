namespace NexJob.Dashboard;

/// <summary>Explains an orphaned queue (jobs waiting, no live node polls it) and how to bring it back.</summary>
internal static class OrphanQueueHint
{
    private const int MaxListed = 5;
    private const string DefaultSuffix = ".default";

    /// <summary>Builds the plain-text hint for an orphaned queue; callers HTML-escape it.</summary>
    /// <param name="orphanQueue">The queue that holds jobs and has no node.</param>
    /// <param name="polledQueues">The queues the live nodes poll.</param>
    /// <returns>The hint, or <see langword="null"/> when no node is alive to compare with.</returns>
    internal static string? For(string orphanQueue, IReadOnlyCollection<string> polledQueues)
    {
        ArgumentNullException.ThrowIfNull(orphanQueue);
        ArgumentNullException.ThrowIfNull(polledQueues);
        if (polledQueues.Count == 0)
        {
            return null;
        }

        var listed = string.Join(", ", polledQueues.Take(MaxListed));
        if (polledQueues.Count > MaxListed)
        {
            listed += $", and {polledQueues.Count - MaxListed} more";
        }

        var hint = $"Live nodes poll: {listed}.";
        var prefix = PrefixOf(orphanQueue);
        return prefix is null
            ? hint
            : $"{hint} If the QueuePrefix or the entry assembly name changed, set QueuePrefix = {prefix} to drain it.";
    }

    private static string? PrefixOf(string queue)
    {
        if (!queue.EndsWith(DefaultSuffix, StringComparison.Ordinal))
        {
            return null;
        }

        var prefix = queue[..^DefaultSuffix.Length];
        return prefix.Length == 0 || prefix.EndsWith('.') ? null : prefix;
    }
}
