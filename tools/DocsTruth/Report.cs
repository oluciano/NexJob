using System.Text;

namespace NexJob.DocsTruth;

/// <summary>Turns findings into a report and an exit code.</summary>
internal static class Report
{
    /// <summary>Renders the findings as Markdown.</summary>
    /// <param name="findings">The findings.</param>
    /// <returns>The report.</returns>
    internal static string Render(IReadOnlyList<Finding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        var text = new StringBuilder("# Docs truth report\n\n");
        if (findings.Count == 0)
        {
            return text.Append("No drift found.\n").ToString();
        }

        var drift = findings.Count(f => f.Severity == Severity.Drift);
        text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{drift} drift, {findings.Count - drift} to review.\n");
        foreach (var group in findings.GroupBy(f => f.Check, StringComparer.Ordinal))
        {
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"\n## {group.Key} ({group.Count()})\n\n");
            foreach (var finding in group.OrderBy(f => f.Severity))
            {
                var label = finding.Severity == Severity.Drift ? "Drift" : "Review";
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"- **{label}** `{finding.Subject}`: {finding.Message}\n");
            }
        }

        return text.ToString();
    }

    /// <summary>The exit code: 1 only with <paramref name="strict"/> and at least one drift finding.</summary>
    /// <param name="findings">The findings.</param>
    /// <param name="strict">Whether drift fails the run.</param>
    /// <returns>The exit code.</returns>
    internal static int ExitCode(IReadOnlyList<Finding> findings, bool strict)
    {
        ArgumentNullException.ThrowIfNull(findings);
        return strict && findings.Any(f => f.Severity == Severity.Drift) ? 1 : 0;
    }
}
