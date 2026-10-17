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
    public async Task ExecutionRow_OpensTheJobDetail_LikeTheJobIdLink()
    {
        // N1 (Positive, #555): clicking an execution row goes where the Job ID link goes.
        var html = await GetDetailAsync(string.Empty, succeeded: 1, failed: 0);

        var start = html.IndexOf("data-href=\"/dashboard/jobs/", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        var url = html.Substring(start + "data-href=\"".Length, "/dashboard/jobs/".Length + 36);
        html.Should().Contain($"<a href=\"{url}\"");
    }

    [Fact]
    public async Task ExecutionRow_WithNoExecutions_HasNoClickableRow()
    {
        // N2 (Negative): an empty history renders no row link.
        var html = await GetDetailAsync(string.Empty, succeeded: 0, failed: 0);

        html.Should().NotContain("data-href=\"/dashboard/jobs/");
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

    private const string Hostile = "a'b\"c\\d<e>&f";

    [Theory]
    [InlineData("daily-report")]
    [InlineData(Hostile)]
    public async Task TriggerNow_Confirmation_IsACompleteHandlerWithTheScheduleId(string id)
    {
        // N1 + N2 (#592): the confirmation is a complete handler and its text carries the id as it was stored.
        var html = await DashboardTestHost.GetAsync($"/dashboard/recurring/{Uri.EscapeDataString(id)}", s => SeedAsync(s, id));

        var confirms = InlineHandlers.Confirms(html);
        confirms.Should().NotContain(h => InlineHandlers.ConfirmMessage(h) == null);
        confirms.Select(h => InlineHandlers.ConfirmMessage(h)).Should().Contain($"Trigger '{id}' now?");
    }

    [Fact]
    public async Task TriggerNow_ConfirmationOfAnEmptyHistory_IsStillACompleteHandler()
    {
        // N3 (Boundary): a schedule that never ran still renders a complete handler.
        var html = await DashboardTestHost.GetAsync("/dashboard/recurring/never-ran", s => SeedAsync(s, "never-ran"));

        InlineHandlers.Confirms(html).Should().NotBeEmpty();
        InlineHandlers.Confirms(html).Should().NotContain(h => InlineHandlers.ConfirmMessage(h) == null);
    }

    private static Task SeedAsync(NexJob.Storage.IStorageProvider s, string id) =>
        s.UpsertRecurringJobAsync(new RecurringJobRecord
        {
            RecurringJobId = id,
            JobType = "Acme.Jobs.ReportJob",
            Cron = "0 3 * * *",
            Queue = "default",
            CreatedAt = DateTimeOffset.UtcNow,
        });
}
