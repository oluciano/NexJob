using FluentAssertions;
using NexJob.DocsTruth;
using Xunit;

namespace NexJob.Tests.DocsTruth;

/// <summary>Tests for <see cref="StaleTermsCheck"/> (#400).</summary>
public sealed class StaleTermsCheckTests
{
    private static readonly StaleTerm Always = new("always-default", "always uses\\s+`?default`?", "6.0.0");

    /// <summary>N1: a line with a stale phrase is reported with its file and line number.</summary>
    [Fact]
    public void Run_StalePhrase_IsReportedWithFileAndLine()
    {
        var files = new[] { ("docs/a.md", "first\nit always uses `default` here\nlast") };

        var findings = StaleTermsCheck.Run(files, [Always], new Version(6, 0, 0));

        var finding = findings.Should().ContainSingle().Subject;
        finding.Subject.Should().Be("docs/a.md:2");
        finding.Check.Should().Be("stale-terms");
        finding.Severity.Should().Be(Severity.Drift);
        finding.Message.Should().Contain("always-default");
    }

    /// <summary>N2: a term that takes effect in a later version is not applied yet.</summary>
    [Fact]
    public void Run_TermNotYetInForce_IsIgnored()
    {
        var files = new[] { ("docs/a.md", "it always uses `default`") };

        StaleTermsCheck.Run(files, [Always], new Version(5, 10, 0)).Should().BeEmpty();
    }

    /// <summary>N2: clean files report nothing.</summary>
    [Fact]
    public void Run_NoMatch_ReportsNothing()
    {
        StaleTermsCheck.Run([("docs/a.md", "nothing special")], [Always], new Version(6, 0, 0)).Should().BeEmpty();
    }

    /// <summary>N3: no files, no terms, and an invalid pattern.</summary>
    [Fact]
    public void Run_EdgeInputs_AreHandled()
    {
        StaleTermsCheck.Run([], [Always], new Version(6, 0, 0)).Should().BeEmpty();
        StaleTermsCheck.Run([("a.md", "text")], [], new Version(6, 0, 0)).Should().BeEmpty();

        var act = () => StaleTermsCheck.Run([("a.md", "text")], [new StaleTerm("bad", "(", "1.0.0")], new Version(6, 0, 0));

        act.Should().Throw<ArgumentException>().WithMessage("*bad*");
    }
}
