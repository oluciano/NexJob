using FluentAssertions;
using Xunit;

namespace NexJob.Tests;

public sealed class OverviewPageTests
{
    [Fact]
    public async Task RecentJobsRow_OpensTheJobDetail_LikeTheViewButton()
    {
        // N1 (Positive, #555): clicking a Recent Jobs row goes where the View button goes.
        var job = DashboardTestHost.Job("Acme.Jobs.ReportJob", "default", JobStatus.Succeeded);
        var html = await DashboardTestHost.GetAsync("/dashboard", s => s.EnqueueAsync(job));

        html.Should().Contain($"data-href=\"/dashboard/jobs/{job.Id.Value}\"");
        html.Should().Contain($"href=\"/dashboard/jobs/{job.Id.Value}\" class=\"btn btn-secondary btn-sm\">View</a>");
    }

    [Fact]
    public async Task RecentJobsRow_WithoutJobs_HasNoClickableRow()
    {
        // N2 (Negative): an empty Recent Jobs table renders no row link.
        var html = await DashboardTestHost.GetAsync("/dashboard");

        html.Should().Contain("No jobs found.");
        html.Should().NotContain("data-href=\"/dashboard/jobs/");
    }

    [Fact]
    public async Task RecentJobsRow_JobTypeWithMarkup_IsEncoded()
    {
        // N3 (Invalid Input): a hostile job type cannot inject markup into the row.
        var html = await DashboardTestHost.GetAsync("/dashboard", s =>
            s.EnqueueAsync(DashboardTestHost.Job("Acme.<script>x</script>", "default", JobStatus.Succeeded)));

        html.Should().NotContain("<script>x</script>");
    }
}
