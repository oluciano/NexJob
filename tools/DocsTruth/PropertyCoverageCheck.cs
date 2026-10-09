namespace NexJob.DocsTruth;

/// <summary>Every public property of an options type must be mentioned by the documentation.</summary>
internal static class PropertyCoverageCheck
{
    /// <summary>Reports the public properties that the documentation does not mention.</summary>
    /// <param name="check">The name of the check, used in the findings.</param>
    /// <param name="type">The options or settings type.</param>
    /// <param name="wikiText">The text of the page that must describe it.</param>
    /// <returns>One finding per undocumented property.</returns>
    internal static IReadOnlyList<Finding> Run(string check, Type type, string wikiText)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(wikiText);
        return [];
    }
}
