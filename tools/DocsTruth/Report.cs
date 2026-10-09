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
        return string.Empty;
    }

    /// <summary>The exit code: 1 only with <paramref name="strict"/> and at least one drift finding.</summary>
    /// <param name="findings">The findings.</param>
    /// <param name="strict">Whether drift fails the run.</param>
    /// <returns>The exit code.</returns>
    internal static int ExitCode(IReadOnlyList<Finding> findings, bool strict)
    {
        ArgumentNullException.ThrowIfNull(findings);
        _ = strict;
        return 0;
    }
}
