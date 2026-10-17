using FluentAssertions;
using Xunit;

namespace NexJob.Tests;

public sealed class CatalogPageTests
{
    private const string JobType = "Acme.Jobs.ReportJob";

    [Fact]
    public async Task Row_OpensHistory_WithTheSameUrlAsTheHistoryButton()
    {
        // N1 (Positive, #555): the row carries the history URL the History button already uses.
        var html = await DashboardTestHost.GetAsync("/dashboard/catalog", s =>
            s.EnqueueAsync(DashboardTestHost.Job(JobType, "reports", JobStatus.Succeeded)));

        html.Should().Contain("data-href=\"/dashboard/jobs?search=ReportJob&amp;queue=reports\"");
        html.Should().Contain("href=\"/dashboard/jobs?search=ReportJob&queue=reports\"");
    }

    [Fact]
    public async Task Shell_RowClick_IgnoresButtonsLinksAndForms()
    {
        // N2 (Negative): a click on Trigger/History inside the row must not also navigate.
        var html = await DashboardTestHost.GetAsync("/dashboard/catalog");

        html.Should().Contain("tr[data-href]");
        html.Should().Contain("closest('a,button,form,input,select,textarea,summary')");
    }

    [Fact]
    public async Task Row_QueueWithMarkup_IsEncodedInTheRowUrl()
    {
        // N3 (Invalid Input): a hostile queue name cannot break out of the data-href attribute.
        var html = await DashboardTestHost.GetAsync("/dashboard/catalog", s =>
            s.EnqueueAsync(DashboardTestHost.Job(JobType, "q\"><script>x</script>", JobStatus.Succeeded)));

        html.Should().NotContain("<script>x</script>");
    }
}
