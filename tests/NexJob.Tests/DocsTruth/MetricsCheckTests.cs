using FluentAssertions;
using NexJob.DocsTruth;
using Xunit;

namespace NexJob.Tests.DocsTruth;

/// <summary>Tests for <see cref="MetricsCheck"/> (#400).</summary>
public sealed class MetricsCheckTests
{
    /// <summary>N1: the real meter yields counters, the histogram and the observable gauges.</summary>
    [Fact]
    public void InstrumentNames_OfTheRealAssembly_IncludesCountersHistogramAndGauges()
    {
        var names = MetricsCheck.InstrumentNames(typeof(NexJobOptions).Assembly);

        names.Should().Contain("nexjob.jobs.succeeded");
        names.Should().Contain("nexjob.job.duration");
        names.Should().Contain("nexjob.queue.depth");
        names.Should().Contain("nexjob.recurring.id_collisions");
    }

    /// <summary>N1: a metric the page does not mention is reported by name.</summary>
    [Fact]
    public void Run_MetricMissingFromTheWiki_IsReported()
    {
        var findings = MetricsCheck.Run(["nexjob.a", "nexjob.b"], "| `nexjob.a` | Counter |");

        var finding = findings.Should().ContainSingle().Subject;
        finding.Subject.Should().Be("nexjob.b");
        finding.Severity.Should().Be(Severity.Drift);
        finding.Check.Should().Be("metrics");
    }

    /// <summary>N2: nothing is reported when every metric is documented.</summary>
    [Fact]
    public void Run_AllMetricsDocumented_ReportsNothing()
    {
        MetricsCheck.Run(["nexjob.a", "nexjob.b"], "`nexjob.a` and `nexjob.b`").Should().BeEmpty();
    }

    /// <summary>N2: a metric whose name only starts like a documented one is still missing.</summary>
    [Fact]
    public void Run_MetricNamePrefixOfADocumentedOne_IsStillReported()
    {
        MetricsCheck.Run(["nexjob.jobs"], "`nexjob.jobs.succeeded`").Should().ContainSingle();
    }

    /// <summary>N3: no instruments means nothing to report; an empty page reports every instrument.</summary>
    [Fact]
    public void Run_EmptyInputs_AreHandled()
    {
        MetricsCheck.Run([], "anything").Should().BeEmpty();
        MetricsCheck.Run(["nexjob.a", "nexjob.b"], string.Empty).Should().HaveCount(2);
    }
}
