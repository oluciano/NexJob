using System.Reflection;

namespace NexJob.Internal;

/// <summary>Maps user-facing queue names to the names stored with jobs.</summary>
internal static class QueueNames
{
    /// <summary>Derives the automatic prefix from the host's entry assembly: its full name, lowercase.</summary>
    /// <param name="entryAssembly">The entry assembly, or <see langword="null"/> when there is none.</param>
    /// <returns>The prefix, or <see langword="null"/> when it cannot be derived.</returns>
    internal static string? DerivePrefix(Assembly? entryAssembly) => default;

    /// <summary>Maps a user-facing queue name to the name stored with the job.</summary>
    /// <param name="queue">The queue the caller asked for, or <see langword="null"/> for the implicit default.</param>
    /// <param name="prefix">The effective prefix.</param>
    /// <returns>The stored queue name.</returns>
    internal static string Resolve(string? queue, string? prefix) => queue ?? "default";
}
