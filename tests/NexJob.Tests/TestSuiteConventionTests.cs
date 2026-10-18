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
