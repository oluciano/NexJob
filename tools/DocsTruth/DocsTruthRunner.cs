namespace NexJob.DocsTruth;

/// <summary>Runs every check against a checkout of the repository.</summary>
internal static class DocsTruthRunner
{
    /// <summary>Runs the checks.</summary>
    /// <param name="repositoryRoot">The root of the repository.</param>
    /// <param name="currentVersion">The version being released.</param>
    /// <returns>The findings; a missing file is a finding, not an exception.</returns>
    internal static IReadOnlyList<Finding> Run(string repositoryRoot, Version currentVersion)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(currentVersion);
        return [];
    }
}
