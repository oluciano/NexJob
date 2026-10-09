using System.Text.Json;
using NexJob.Configuration;

namespace NexJob.DocsTruth;

/// <summary>Runs every check against a checkout of the repository.</summary>
internal static class DocsTruthRunner
{
    private const string MetricsPage = "docs/wiki/integrations/opentelemetry.md";
    private const string ConfigurationPage = "docs/wiki/reference/configuration.md";
    private const string StaleTermsFile = "tools/DocsTruth/stale-terms.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Runs the checks.</summary>
    /// <param name="repositoryRoot">The root of the repository.</param>
    /// <param name="currentVersion">The version being released.</param>
    /// <returns>The findings; a missing file is a finding, not an exception.</returns>
    internal static IReadOnlyList<Finding> Run(string repositoryRoot, Version currentVersion)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(currentVersion);

        var findings = new List<Finding>();
        var metricsText = ReadPage(repositoryRoot, MetricsPage, findings);
        var configurationText = ReadPage(repositoryRoot, ConfigurationPage, findings);

        if (metricsText is not null)
        {
            findings.AddRange(MetricsCheck.Run(MetricsCheck.InstrumentNames(typeof(NexJobOptions).Assembly), metricsText));
        }

        if (configurationText is not null)
        {
            findings.AddRange(PropertyCoverageCheck.Run("options", typeof(NexJobOptions), configurationText));
            findings.AddRange(PropertyCoverageCheck.Run("settings", typeof(NexJobSettings), configurationText));
            findings.AddRange(DefaultsCheck.Run(new NexJobOptions(), configurationText));
        }

        findings.AddRange(StaleTermsCheck.Run(SearchableFiles(repositoryRoot), LoadStaleTerms(repositoryRoot), currentVersion));
        return findings;
    }

    private static string? ReadPage(string root, string relativePath, List<Finding> findings)
    {
        var path = Path.Combine(root, relativePath);
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        findings.Add(new Finding("files", relativePath, "The page the check reads was not found."));
        return null;
    }

    private static IReadOnlyList<StaleTerm> LoadStaleTerms(string root)
    {
        var path = Path.Combine(root, StaleTermsFile);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<List<StaleTerm>>(File.ReadAllText(path), JsonOptions) ?? []
            : [];
    }

    private static IEnumerable<(string Path, string Text)> SearchableFiles(string root)
    {
        var wiki = Path.Combine(root, "docs", "wiki");
        if (Directory.Exists(wiki))
        {
            foreach (var file in Directory.EnumerateFiles(wiki, "*.md", SearchOption.AllDirectories))
            {
                yield return (Relative(root, file), File.ReadAllText(file));
            }
        }

        var src = Path.Combine(root, "src");
        if (!Directory.Exists(src))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories).Where(IsSource))
        {
            var text = File.ReadAllText(file);
            yield return (Relative(root, file), file.EndsWith(".cs", StringComparison.Ordinal) ? XmlDocLines(text) : text);
        }
    }

    private static bool IsSource(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("/obj/", StringComparison.Ordinal) || normalized.Contains("/bin/", StringComparison.Ordinal))
        {
            return false;
        }

        return normalized.EndsWith(".cs", StringComparison.Ordinal) || normalized.EndsWith("/README.md", StringComparison.Ordinal);
    }

    private static string XmlDocLines(string text) =>
        string.Join('\n', text.Split('\n').Select(line => line.TrimStart().StartsWith("///", StringComparison.Ordinal) ? line : string.Empty));

    private static string Relative(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');
}
