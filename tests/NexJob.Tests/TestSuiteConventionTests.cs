using FluentAssertions;
using Xunit;

namespace NexJob.Tests;

/// <summary>
/// Architecture guard: every production class has one canonical test file. Parallel "hardening"
/// files duplicate scenarios and inflate the suite (issue #379).
/// </summary>
public sealed class TestSuiteConventionTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "nexjob-conv-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    // N1 — Positive: the real repository contains no twin test files.
    [Fact]
    public void Repository_HasNoHardeningTestFiles()
    {
        var root = FindRepoRoot();

        var offenders = FindForbiddenTestFiles(Path.Combine(root, "tests"));

        offenders.Should().BeEmpty(
            "parallel '*HardeningTests.cs' files are banned; put edge cases in the canonical '<Class>Tests.cs'");
    }

    // N2 — Negative: a twin file is detected, including in nested folders.
    [Fact]
    public void Scanner_DetectsHardeningFile_InNestedFolder()
    {
        var nested = Directory.CreateDirectory(Path.Combine(_tempRoot, "NexJob.Tests", "Sub")).FullName;
        File.WriteAllText(Path.Combine(nested, "FooHardeningTests.cs"), string.Empty);

        var offenders = FindForbiddenTestFiles(_tempRoot);

        offenders.Should().ContainSingle().Which.Should().EndWith("FooHardeningTests.cs");
    }

    // N3 — Invalid input: canonical names, build output and a missing directory are not offenders.
    [Fact]
    public void Scanner_IgnoresCanonicalFiles_BuildOutput_AndMissingDirectory()
    {
        var project = Directory.CreateDirectory(Path.Combine(_tempRoot, "NexJob.Tests")).FullName;
        File.WriteAllText(Path.Combine(project, "FooTests.cs"), string.Empty);
        var obj = Directory.CreateDirectory(Path.Combine(project, "obj")).FullName;
        File.WriteAllText(Path.Combine(obj, "FooHardeningTests.cs"), string.Empty);

        FindForbiddenTestFiles(_tempRoot).Should().BeEmpty();
        FindForbiddenTestFiles(Path.Combine(_tempRoot, "does-not-exist")).Should().BeEmpty();
    }

    // N1 — Positive: every rule the repository turns off has a reason next to it (issue #586).
    [Fact]
    public void EditorConfig_EverySilencedRuleHasAReason()
    {
        var text = File.ReadAllText(Path.Combine(FindRepoRoot(), ".editorconfig"));

        FindSilencedRulesWithoutReason(text).Should().BeEmpty("a rule turned off without a reason is a decision nobody can review");
    }

    // N2 — Negative: a rule turned off with no comment in its block or above its section is reported.
    [Fact]
    public void Scanner_ReportsSilencedRule_WithoutComment()
    {
        const string text = "[*.cs]\ndotnet_diagnostic.CA1234.severity = none\n\n# why\n[tests/**/*.cs]\ndotnet_diagnostic.CA5678.severity = none\n";

        FindSilencedRulesWithoutReason(text).Should().Equal("dotnet_diagnostic.CA1234.severity = none");
    }

    // N3 — Invalid input: empty text, and a reason written either above the section or inside the block.
    [Theory]
    [InlineData("")]
    [InlineData("# why\n[*.cs]\ndotnet_diagnostic.CA1.severity = none\n")]
    [InlineData("[*.cs]\n# why\ndotnet_diagnostic.CA1.severity = none\n")]
    public void Scanner_AcceptsEmptyText_AndReasonsInEitherPlace(string text)
    {
        FindSilencedRulesWithoutReason(text).Should().BeEmpty();
    }

    // N1/N2 (issue #585) — Positive: README, wiki and package READMEs never state net8.0 as the only runtime.
    [Fact]
    public void Docs_DoNotStateNet8AsTheOnlyRuntime()
    {
        var root = FindRepoRoot();
        var files = new[] { Path.Combine(root, "README.md") }
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "docs", "wiki"), "*.md", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "src"), "README.md", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)));

        var stale = files.SelectMany(f => FindStaleRuntimeStatements(File.ReadAllText(f)).Select(l => $"{Path.GetRelativePath(root, f)}: {l}")).ToList();

        stale.Should().BeEmpty("the packages target net8.0 and net10.0");
    }

    // N2 — Negative: the scanner reports the stale phrasings.
    [Theory]
    [InlineData("A library for .NET 8+.")]
    [InlineData("NexJob is a job library for .NET 8 built for jobs.")]
    [InlineData("The packages run on net8.0 only.")]
    [InlineData("Supported: .NET 8 only")]
    public void Scanner_ReportsStaleRuntimeStatement(string line)
    {
        FindStaleRuntimeStatements(line).Should().ContainSingle();
    }

    // N3 — Invalid input: empty text and the current wording are accepted.
    [Theory]
    [InlineData("")]
    [InlineData("A library for .NET 8 and .NET 10.")]
    [InlineData("Measured on .NET 8.0.31 in-memory.")]
    public void Scanner_AcceptsEmptyText_AndTheCurrentWording(string text)
    {
        FindStaleRuntimeStatements(text).Should().BeEmpty();
    }

    // N3 (issue #585): the runtimes in the SECURITY.md table are the TargetFrameworks of the packages.
    [Fact]
    public void SecurityPolicy_SupportedRuntimes_MatchTheTargetFrameworks()
    {
        var root = FindRepoRoot();
        var props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));
        var targets = System.Text.RegularExpressions.Regex.Match(props, "<TargetFrameworks>(?<tfms>[^<]+)</TargetFrameworks>", System.Text.RegularExpressions.RegexOptions.None, RegexTimeout).Groups["tfms"].Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var security = File.ReadAllText(Path.Combine(root, ".github", "SECURITY.md"));
        var section = security[security.IndexOf("## Supported runtimes", StringComparison.Ordinal)..];
        var listed = System.Text.RegularExpressions.Regex.Matches(section.Split("## Reporting", StringSplitOptions.None)[0], @"\| `(?<tfm>net[0-9.]+)` \| Yes", System.Text.RegularExpressions.RegexOptions.None, RegexTimeout)
            .Select(m => m.Groups["tfm"].Value);

        listed.Should().BeEquivalentTo(targets);
    }

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static List<string> FindStaleRuntimeStatements(string text)
    {
        var stale = new System.Text.RegularExpressions.Regex(
            @"for \.NET 8\+|for \.NET 8(?! and| or| \()\b|net8\.0 only|\.NET 8 only",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            RegexTimeout);
        return text.Split('\n').Where(l => stale.IsMatch(l)).Select(l => l.Trim()).ToList();
    }

    private static List<string> FindSilencedRulesWithoutReason(string editorConfig)
    {
        var lines = editorConfig.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        var missing = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("dotnet_diagnostic.", StringComparison.Ordinal)
                || !lines[i].EndsWith("severity = none", StringComparison.Ordinal))
            {
                continue;
            }

            var hasReason = false;
            for (var j = i - 1; j >= 0 && lines[j].Length > 0; j--)
            {
                if (lines[j].StartsWith('#'))
                {
                    hasReason = true;
                    break;
                }

                if (lines[j].StartsWith('['))
                {
                    hasReason = j > 0 && lines[j - 1].StartsWith('#');
                    break;
                }
            }

            if (!hasReason)
            {
                missing.Add(lines[i]);
            }
        }

        return missing;
    }

    private static List<string> FindForbiddenTestFiles(string testsDirectory)
    {
        if (!Directory.Exists(testsDirectory))
        {
            return new List<string>();
        }

        var separator = Path.DirectorySeparatorChar;
        return Directory
            .GetFiles(testsDirectory, "*HardeningTests.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                     && !f.Contains($"{separator}bin{separator}", StringComparison.Ordinal))
            .ToList();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NexJob.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("NexJob.sln not found above the test binaries.");
    }
}
