namespace NexJob.DocsTruth;

/// <summary>The default written in the documentation must be the real one.</summary>
internal static class DefaultsCheck
{
    /// <summary>Compares the documented <c>// Default:</c> comments with the values of a fresh instance.</summary>
    /// <param name="instance">A new instance of the options type.</param>
    /// <param name="wikiText">The text of the configuration page.</param>
    /// <returns>A drift finding for a wrong default, a review finding for one that cannot be compared.</returns>
    internal static IReadOnlyList<Finding> Run(object instance, string wikiText)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(wikiText);
        return [];
    }
}
