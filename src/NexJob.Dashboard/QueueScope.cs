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
        if (scope is not { Count: > 0 })
        {
            return scope;
        }

        var result = new List<string>(scope.Count + 1);
        foreach (var entry in scope)
        {
            if (string.Equals(entry, "default", StringComparison.OrdinalIgnoreCase))
            {
                AddOnce(result, options.ResolveQueue(null));
                AddOnce(result, "default");
            }
            else
            {
                AddOnce(result, entry);
            }
        }

        return result;
    }

    /// <summary>
    /// Like <see cref="Expand"/>, but the legacy <c>default</c> that the alias added is left out while no job is
    /// stored in it, so an emptied legacy queue does not stay in the dashboard as a zero row.
    /// </summary>
    /// <param name="scope">The configured scope, or <see langword="null"/> for all queues.</param>
    /// <param name="options">The options of the host the dashboard runs in.</param>
    /// <param name="stored">The queues that hold jobs.</param>
    /// <returns>The stored queue names to show.</returns>
    internal static IReadOnlyList<string>? Resolve(IReadOnlyList<string>? scope, NexJobOptions options, IEnumerable<QueueMetrics> stored)
    {
        var expanded = Expand(scope, options);
        var aliased = !string.Equals(options.ResolveQueue(null), "default", StringComparison.Ordinal);
        if (expanded is null || !aliased || stored.Any(q => string.Equals(q.Queue, "default", StringComparison.Ordinal)))
        {
            return expanded;
        }

        return expanded.Where(q => !string.Equals(q, "default", StringComparison.Ordinal)).ToList();
    }

    private static void AddOnce(List<string> list, string name)
    {
        if (!list.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(name);
        }
    }
}
