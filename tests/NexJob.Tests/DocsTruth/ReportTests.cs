using FluentAssertions;
using NexJob.DocsTruth;
using Xunit;

namespace NexJob.Tests.DocsTruth;

/// <summary>Tests for <see cref="Report"/> (#400).</summary>
public sealed class ReportTests
{
    private static readonly Finding Drift = new("metrics", "nexjob.x", "not documented");
    private static readonly Finding Review = new("defaults", "Queues", "cannot compare", Severity.Review);

    /// <summary>N1: findings are listed under their check, drift and review apart.</summary>
    [Fact]
    public void Render_WithFindings_GroupsThemByCheck()
    {
        var text = Report.Render([Drift, Review]);

        text.Should().Contain("metrics").And.Contain("nexjob.x").And.Contain("not documented");
        text.Should().Contain("defaults").And.Contain("Queues");
        text.IndexOf("metrics", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("defaults", StringComparison.Ordinal));
    }

    /// <summary>N2: an empty list says there is no drift.</summary>
    [Fact]
    public void Render_NoFindings_SaysThereIsNoDrift()
    {
        Report.Render([]).Should().Contain("No drift");
    }

    /// <summary>N1/N2: only drift under --strict fails the run.</summary>
    /// <param name="strict">The flag.</param>
    /// <param name="hasDrift">Whether there is a drift finding.</param>
    /// <param name="expected">The expected exit code.</param>
    [Theory]
    [InlineData(true, true, 1)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    [InlineData(false, false, 0)]
    public void ExitCode_FailsOnlyOnDriftUnderStrict(bool strict, bool hasDrift, int expected)
    {
        IReadOnlyList<Finding> findings = hasDrift ? [Drift, Review] : [Review];

        Report.ExitCode(findings, strict).Should().Be(expected);
    }

    /// <summary>N3: no findings at all never fail.</summary>
    [Fact]
    public void ExitCode_NoFindings_IsZero()
    {
        Report.ExitCode([], strict: true).Should().Be(0);
    }
}
