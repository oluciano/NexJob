using FluentAssertions;
using Xunit;

namespace NexJob.Tests;

public sealed class HtmlFragmentsTests
{
    private const string JobType = "Acme.Jobs.ReportJob";

    [Theory]
    [InlineData("/dashboard/jobs")]
    [InlineData("/dashboard/failed")]
    public async Task FilterBar_QueueNameWithMarkup_IsShownAsText(string route)
    {
        // N1 (Positive, #589): the queue option text is HTML-encoded, so markup in a queue name is text, not markup.
        var html = await DashboardTestHost.GetAsync(route, s =>
            s.EnqueueAsync(DashboardTestHost.Job(JobType, "q</option><b id=\"injected\">x</b>", JobStatus.Failed)));

        html.Should().NotContain("<b id=\"injected\">");
        html.Should().Contain("q&lt;/option&gt;&lt;b id=&quot;injected&quot;&gt;x&lt;/b&gt;");
    }

    [Fact]
    public async Task FilterBar_NormalQueue_ShowsItsNameAndCount()
    {
        // N2 (Positive): a normal queue still shows its name and its job count in the option.
        var html = await DashboardTestHost.GetAsync("/dashboard/jobs", s =>
            s.EnqueueAsync(DashboardTestHost.Job(JobType, "reports", JobStatus.Enqueued)));

        html.Should().Contain("<option value=\"reports\">reports (1)</option>");
    }

    [Fact]
    public async Task FilterBar_WithNoQueues_RendersNoQueueOption()
    {
        // N3 (Boundary): an empty storage renders the filter without any queue option and without an error.
        var html = await DashboardTestHost.GetAsync("/dashboard/jobs");

        html.Should().Contain("name=\"queue\"");
        html.Should().NotContain("<option value=\"reports\"");
    }
}
