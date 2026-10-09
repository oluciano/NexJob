using System.Reflection;

namespace NexJob.Internal;

/// <summary>Maps user-facing queue names to the names stored with jobs.</summary>
internal static class QueueNames
{
    /// <summary>The queue used when none is named, and the legacy queue every host drains.</summary>
    internal const string Default = "default";

    /// <summary>Derives the automatic prefix from the host's entry assembly: its full name, lowercase.</summary>
    /// <param name="entryAssembly">The entry assembly, or <see langword="null"/> when there is none.</param>
    /// <returns>The prefix, or <see langword="null"/> when it cannot be derived.</returns>
    internal static string? DerivePrefix(Assembly? entryAssembly)
    {
        var name = entryAssembly?.GetName().Name;
        return string.IsNullOrWhiteSpace(name) ? null : name.ToLowerInvariant();
    }

    /// <summary>
    /// Maps a user-facing queue name to the name stored with the job. Only the implicit default is prefixed:
    /// named queues, and names that already contain a dot, are returned as they are.
    /// </summary>
    /// <param name="queue">The queue the caller asked for, or <see langword="null"/> for the implicit default.</param>
    /// <param name="prefix">The effective prefix; <see langword="null"/> or blank disables prefixing.</param>
    /// <returns>The stored queue name.</returns>
    internal static string Resolve(string? queue, string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix) || (queue is not null && !string.Equals(queue, Default, StringComparison.Ordinal)))
        {
            return queue ?? Default;
        }

        return string.Concat(prefix, ".", Default);
    }
}
