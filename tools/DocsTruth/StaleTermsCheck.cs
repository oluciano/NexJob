using System.Text.RegularExpressions;

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

        var active = terms
            .Select(term => (Term: term, Regex: Compile(term), Since: Version.Parse(term.Since)))
            .Where(t => currentVersion >= t.Since)
            .ToList();
        var findings = new List<Finding>();
        foreach (var (path, text) in files)
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var (term, regex, _) in active.Where(t => t.Regex.IsMatch(lines[i])))
                {
                    findings.Add(new Finding(
                        "stale-terms",
                        $"{path}:{i + 1}",
                        $"Matches the stale term '{term.Name}' (wrong since {term.Since}): {lines[i].Trim()}"));
                }
            }
        }

        return findings;
    }

    private static Regex Compile(StaleTerm term)
    {
        try
        {
            return new Regex(term.Pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Invalid pattern for the stale term '{term.Name}': {ex.Message}", nameof(term), ex);
        }
    }
}
