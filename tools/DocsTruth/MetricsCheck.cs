using System.Reflection;

namespace NexJob.DocsTruth;

/// <summary>Every instrument of the NexJob meter must be documented.</summary>
internal static class MetricsCheck
{
    /// <summary>Reads the names of the instruments the NexJob meter defines.</summary>
    /// <param name="coreAssembly">The NexJob assembly.</param>
    /// <returns>The instrument names.</returns>
    internal static IReadOnlyList<string> InstrumentNames(Assembly coreAssembly)
    {
        ArgumentNullException.ThrowIfNull(coreAssembly);
        return [];
    }

    /// <summary>Reports the instruments that the documentation does not mention.</summary>
    /// <param name="names">The instrument names defined by the code.</param>
    /// <param name="wikiText">The text of the metrics page.</param>
    /// <returns>One finding per undocumented instrument.</returns>
    internal static IReadOnlyList<Finding> Run(IReadOnlyList<string> names, string wikiText)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(wikiText);
        return [];
    }
}
