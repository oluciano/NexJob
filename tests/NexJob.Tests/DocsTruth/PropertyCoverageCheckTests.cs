using FluentAssertions;
using NexJob.DocsTruth;
using Xunit;

namespace NexJob.Tests.DocsTruth;

/// <summary>Tests for <see cref="PropertyCoverageCheck"/> (#400).</summary>
public sealed class PropertyCoverageCheckTests
{
    /// <summary>N1: a property the page does not mention is reported with the check name.</summary>
    [Fact]
    public void Run_PropertyMissingFromTheWiki_IsReported()
    {
        var findings = PropertyCoverageCheck.Run("options", typeof(Sample), "`Workers` is documented");

        var finding = findings.Should().ContainSingle().Subject;
        finding.Subject.Should().Be("QueuePrefix");
        finding.Check.Should().Be("options");
        finding.Severity.Should().Be(Severity.Drift);
    }

    /// <summary>N2: nothing is reported when every property is mentioned.</summary>
    [Fact]
    public void Run_AllPropertiesMentioned_ReportsNothing()
    {
        PropertyCoverageCheck.Run("options", typeof(Sample), "options.Workers = 4; options.QueuePrefix = \"a\";").Should().BeEmpty();
    }

    /// <summary>N2: a longer word that contains the property name does not count as documenting it.</summary>
    [Fact]
    public void Run_NameOnlyInsideALongerWord_IsNotDocumented()
    {
        var findings = PropertyCoverageCheck.Run("options", typeof(Sample), "`WorkersTotal` and `QueuePrefixes`");

        findings.Select(f => f.Subject).Should().BeEquivalentTo("Workers", "QueuePrefix");
    }

    /// <summary>N3: a type without public properties, and an empty page.</summary>
    [Fact]
    public void Run_EdgeInputs_AreHandled()
    {
        PropertyCoverageCheck.Run("options", typeof(Empty), "text").Should().BeEmpty();
        PropertyCoverageCheck.Run("options", typeof(Sample), string.Empty).Should().HaveCount(2);
    }

    /// <summary>N3: internal and static members are not part of the documented surface.</summary>
    [Fact]
    public void Run_InternalAndStaticMembers_AreIgnored()
    {
        PropertyCoverageCheck.Run("options", typeof(WithHidden), "`Visible`").Should().BeEmpty();
    }

    public sealed class Sample
    {
        public int Workers { get; set; }

        public string? QueuePrefix { get; set; }
    }

    public sealed class Empty
    {
    }

    public sealed class WithHidden
    {
        public static int Shared { get; set; }

        public int Visible { get; set; }

        internal int Hidden { get; set; }
    }
}
