using FluentAssertions;
using NexJob.DocsTruth;
using Xunit;

namespace NexJob.Tests.DocsTruth;

/// <summary>Tests for <see cref="DocsTruthRunner"/> (#400).</summary>
public sealed class DocsTruthRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"docs-truth-{Guid.NewGuid():N}");

    /// <summary>N3: a repository without the wiki pages reports the missing files instead of throwing.</summary>
    [Fact]
    public void Run_WithoutTheWikiPages_ReportsMissingFiles()
    {
        Directory.CreateDirectory(_root);

        var findings = DocsTruthRunner.Run(_root, new Version(6, 0, 0));

        findings.Should().Contain(f => f.Check == "files" && f.Subject.EndsWith("opentelemetry.md", StringComparison.Ordinal) && f.Severity == Severity.Drift);
        findings.Should().Contain(f => f.Check == "files" && f.Subject.EndsWith("configuration.md", StringComparison.Ordinal));
    }

    /// <summary>N1: an empty configuration page reports the undocumented options and settings; an empty metrics page the instruments.</summary>
    [Fact]
    public void Run_WithEmptyPages_ReportsTheUndocumentedSurface()
    {
        WritePage("integrations/opentelemetry.md", "nothing here");
        WritePage("reference/configuration.md", "nothing here");

        var findings = DocsTruthRunner.Run(_root, new Version(6, 0, 0));

        findings.Should().Contain(f => f.Check == "metrics" && f.Subject == "nexjob.jobs.succeeded");
        findings.Should().Contain(f => f.Check == "options" && f.Subject == nameof(NexJobOptions.QueuePrefix));
        findings.Should().Contain(f => f.Check == "settings" && f.Subject == nameof(NexJob.Configuration.NexJobSettings.QueuePrefix));
    }

    /// <summary>N2: pages that mention the whole surface report no drift of those checks.</summary>
    [Fact]
    public void Run_WithPagesThatMentionEverything_ReportsNoDriftOfThoseChecks()
    {
        var names = MetricsCheck.InstrumentNames(typeof(NexJobOptions).Assembly);
        var options = typeof(NexJobOptions).GetProperties().Select(p => $"`{p.Name}`");
        var settings = typeof(NexJob.Configuration.NexJobSettings).GetProperties().Select(p => $"`{p.Name}`");
        WritePage("integrations/opentelemetry.md", string.Join(' ', names.Select(n => $"`{n}`")));
        WritePage("reference/configuration.md", string.Join(' ', options.Concat(settings)));

        var findings = DocsTruthRunner.Run(_root, new Version(6, 0, 0));

        findings.Where(f => f.Check is "metrics" or "options" or "settings" or "files").Should().BeEmpty();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void WritePage(string relativePath, string text)
    {
        var path = Path.Combine(_root, "docs", "wiki", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
