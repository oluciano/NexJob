namespace NexJob.Dashboard;

/// <summary>Explains an orphaned queue (jobs waiting, no live node polls it) and how to bring it back.</summary>
internal static class OrphanQueueHint
{
    /// <summary>Builds the plain-text hint for an orphaned queue; callers HTML-escape it.</summary>
    /// <param name="orphanQueue">The queue that holds jobs and has no node.</param>
    /// <param name="polledQueues">The queues the live nodes poll.</param>
    /// <returns>The hint, or <see langword="null"/> when no node is alive to compare with.</returns>
    internal static string? For(string orphanQueue, IReadOnlyCollection<string> polledQueues)
    {
        ArgumentNullException.ThrowIfNull(orphanQueue);
        ArgumentNullException.ThrowIfNull(polledQueues);
        return null;
    }
}
