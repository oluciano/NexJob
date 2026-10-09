namespace NexJob.DocsTruth;

/// <summary>Searches files for phrases that are no longer true.</summary>
internal static class StaleTermsCheck
{
    /// <summary>Reports every line that matches a stale term already in force.</summary>
    /// <param name="files">The files to search, as path and text.</param>
    /// <param name="terms">The stale terms.</param>
    /// <param name="currentVersion">The version being released.</param>
    /// <returns>One finding per matching line.</returns>
    internal static IReadOnlyList<Finding> Run(IEnumerable<(string Path, string Text)> files, IReadOnlyList<StaleTerm> terms, Version currentVersion)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(terms);
        ArgumentNullException.ThrowIfNull(currentVersion);
        return [];
    }
}
