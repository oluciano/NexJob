using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace NexJob.DocsTruth;

/// <summary>Every instrument of the NexJob meter must be documented.</summary>
internal static class MetricsCheck
{
    private const string MetricsTypeName = "NexJob.Telemetry.NexJobMetrics";

    /// <summary>Reads the names of the instruments the NexJob meter defines.</summary>
    /// <param name="coreAssembly">The NexJob assembly.</param>
    /// <returns>The instrument names.</returns>
    internal static IReadOnlyList<string> InstrumentNames(Assembly coreAssembly)
    {
        ArgumentNullException.ThrowIfNull(coreAssembly);
        var metrics = coreAssembly.GetType(MetricsTypeName)
            ?? throw new InvalidOperationException($"{MetricsTypeName} was not found in {coreAssembly.GetName().Name}.");

        // The instruments are internal static fields on purpose: the tool reads exactly what the meter publishes.
#pragma warning disable S3011
        var fields = metrics.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
#pragma warning restore S3011
        return fields
            .Where(f => typeof(Instrument).IsAssignableFrom(f.FieldType))
            .Select(f => ((Instrument)f.GetValue(null)!).Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Reports the instruments that the documentation does not mention.</summary>
    /// <param name="names">The instrument names defined by the code.</param>
    /// <param name="wikiText">The text of the metrics page.</param>
    /// <returns>One finding per undocumented instrument.</returns>
    internal static IReadOnlyList<Finding> Run(IReadOnlyList<string> names, string wikiText)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(wikiText);
        return names
            .Where(name => !IsMentioned(name, wikiText))
            .Select(name => new Finding("metrics", name, "The instrument exists in the code and the metrics page does not mention it."))
            .ToList();
    }

    private static bool IsMentioned(string name, string text) =>
        Regex.IsMatch(text, $@"(?<![\w.]){Regex.Escape(name)}(?!\.?\w)", RegexOptions.None, TimeSpan.FromSeconds(1));
}
