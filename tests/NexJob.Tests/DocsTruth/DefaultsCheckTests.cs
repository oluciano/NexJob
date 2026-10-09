using FluentAssertions;
using NexJob.DocsTruth;
using Xunit;

namespace NexJob.Tests.DocsTruth;

/// <summary>Tests for <see cref="DefaultsCheck"/> (#400).</summary>
public sealed class DefaultsCheckTests
{
    /// <summary>N1: a wrong documented default is drift and shows both values.</summary>
    /// <param name="line">The documented line.</param>
    /// <param name="subject">The property.</param>
    [Theory]
    [InlineData("options.MaxAttempts = 5; // Default: 20", "MaxAttempts")]
    [InlineData("options.Enabled = false; // Default: false", "Enabled")]
    [InlineData("options.Poll = X; // Default: TimeSpan.FromSeconds(30)", "Poll")]
    [InlineData("options.Mode = X; // Default: Fast", "Mode")]
    public void Run_WrongDefault_IsDrift(string line, string subject)
    {
        var findings = DefaultsCheck.Run(new Sample(), line);

        var finding = findings.Should().ContainSingle().Subject;
        finding.Subject.Should().Be(subject);
        finding.Severity.Should().Be(Severity.Drift);
        finding.Check.Should().Be("defaults");
    }

    /// <summary>N2: a documented default equal to the real one is silent.</summary>
    /// <param name="line">The documented line.</param>
    [Theory]
    [InlineData("options.MaxAttempts = 5; // Default: 10")]
    [InlineData("options.Enabled = false; // Default: true")]
    [InlineData("options.Poll = X; // Default: TimeSpan.FromSeconds(15)")]
    [InlineData("options.Mode = X; // Default: Slow")]
    [InlineData("options.Name = X; // Default: \"abc\"")]
    public void Run_CorrectDefault_ReportsNothing(string line)
    {
        DefaultsCheck.Run(new Sample(), line).Should().BeEmpty();
    }

    /// <summary>N3: a default that cannot be compared is a review item, never drift.</summary>
    [Fact]
    public void Run_NonSimpleDefault_IsReview()
    {
        var findings = DefaultsCheck.Run(new Sample(), "options.Queues = X; // Default: [\"default\"]");

        var finding = findings.Should().ContainSingle().Subject;
        finding.Severity.Should().Be(Severity.Review);
        finding.Subject.Should().Be("Queues");
    }

    /// <summary>N3: lines without a Default comment, and properties the type does not have, are ignored.</summary>
    [Fact]
    public void Run_NoDefaultComment_OrUnknownProperty_IsIgnored()
    {
        DefaultsCheck.Run(new Sample(), "options.MaxAttempts = 5;\nplain text\noptions.Nope = 1; // Default: 3").Should().BeEmpty();
        DefaultsCheck.Run(new Sample(), string.Empty).Should().BeEmpty();
    }

    public enum Speed
    {
        Slow,
        Fast,
    }

    public sealed class Sample
    {
        public int MaxAttempts { get; set; } = 10;

        public bool Enabled { get; set; } = true;

        public TimeSpan Poll { get; set; } = TimeSpan.FromSeconds(15);

        public Speed Mode { get; set; } = Speed.Slow;

        public string Name { get; set; } = "abc";

        public IReadOnlyList<string> Queues { get; set; } = ["default"];
    }
}
