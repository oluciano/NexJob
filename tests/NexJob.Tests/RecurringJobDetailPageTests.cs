using FluentAssertions;
using Xunit;

namespace NexJob.Tests;

public sealed class RecurringJobDetailPageTests
{
    private const string JobType = "Acme.Jobs.ReportJob";
    private const string RecurringId = "daily-report";

    [Fact]
    public async Task Detail_ShowsCatalogTotals_LabelledForTheJobType()
    {
        // N1 (Positive, #555): totals come from the catalog item and say they are per job type.
        var html = await GetDetailAsync(string.Empty, succeeded: 2, failed: 1);

        html.Should().Contain("Total Runs");
        html.Should().Contain("for this job type");
        html.Should().MatchRegex(@">\s*3\s*<");
    }

    [Fact]
    public async Task StatusFilter_NarrowsTheExecutionHistory()
    {
        // N1 (Positive): ?status=Failed keeps only the failed run and the select reflects it.
        var html = await GetDetailAsync("?status=Failed", succeeded: 2, failed: 1);

        html.Should().Contain("1 total runs");
        html.Should().Contain("<option value=\"Failed\" selected>");
    }

    [Fact]
    public async Task StatusFilter_WithNoMatch_KeepsFilterAndSaysNothingMatches()
    {
        // N3 (Boundary): an empty filtered table keeps the filter visible so it can be cleared.
        var html = await GetDetailAsync("?status=Failed", succeeded: 2, failed: 0);

        html.Should().Contain("name=\"status\"");
        html.Should().Contain("No executions match");
    }

    [Fact]
    public async Task StatusFilter_UnknownValue_IsIgnored()
    {
        // N3 (Invalid Input): a status that is not a JobStatus shows the unfiltered history.
        var html = await GetDetailAsync("?status=%3Cscript%3E", succeeded: 2, failed: 0);

        html.Should().Contain("2 total runs");
        html.Should().NotContain("<script>x");
    }

    [Fact]
    public async Task Detail_WithNoExecutions_ShowsZeroTotals()
    {
        // N3 (Boundary): a recurring job that never ran shows 0 totals, not an error.
        var html = await GetDetailAsync(string.Empty, succeeded: 0, failed: 0);

        html.Should().Contain("Total Runs");
        html.Should().Contain("No execution history");
    }

    private static Task<string> GetDetailAsync(string query, int succeeded, int failed) =>
        DashboardTestHost.GetAsync($"/dashboard/recurring/{RecurringId}{query}", async s =>
        {
            await s.UpsertRecurringJobAsync(new RecurringJobRecord
            {
                RecurringJobId = RecurringId,
                JobType = JobType,
                Cron = "0 3 * * *",
                Queue = "reports",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            for (var i = 0; i < succeeded; i++)
            {
                await s.EnqueueAsync(DashboardTestHost.Job(JobType, "reports", JobStatus.Succeeded, RecurringId));
            }

            for (var i = 0; i < failed; i++)
            {
                await s.EnqueueAsync(DashboardTestHost.Job(JobType, "reports", JobStatus.Failed, RecurringId));
            }
        });
}
