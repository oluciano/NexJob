using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

internal sealed class FailedPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IDashboardStorage Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/dashboard";
    [Parameter] public string Title { get; set; } = "NexJob";
    [Parameter] public NavCounters? Counters { get; set; }
    [Parameter] public JobStatus? StatusFilter { get; set; }
    [Parameter] public string? Search { get; set; }
    [Parameter] public int Page { get; set; } = 1;

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);

        // Fetch queues for the filter dropdown
        var queues = await Storage.GetQueueMetricsAsync(CancellationToken.None).ConfigureAwait(false);

        // Default to Failed if no status specified; allow Expired as override
        var status = StatusFilter ?? JobStatus.Failed;
        var filter = new JobFilter { Status = status, Search = Search };
        var result = await Storage.GetJobsAsync(filter, Page, 25).ConfigureAwait(false);
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(result, status, queues)));
    }

    private string BuildHtml(PagedResult<JobRecord> result, JobStatus status, IReadOnlyList<QueueMetrics> queues)
    {
        var now = DateTimeOffset.UtcNow;
        var currentStatus = status.ToString();

        var subtitle = status switch
        {
            JobStatus.Failed => "Dead-letter queue",
            JobStatus.Expired => "Deadline exceeded",
            _ => "Failed jobs",
        };

        if (result.Items.Count == 0)
        {
            var emptyMsg = status switch
            {
                JobStatus.Failed => "No failed jobs",
                JobStatus.Expired => "No expired jobs",
                _ => "No jobs found",
            };
            var emptySub = status switch
            {
                JobStatus.Failed => "Everything is running normally.",
                JobStatus.Expired => "All jobs completed before their deadline.",
                _ => "No matching jobs.",
            };

            var emptyBody =
                "<div id=\"failed-page-content\" data-refresh=\"true\">" +
                HtmlFragments.Breadcrumbs(PathPrefix, ("Failed", null)) +
                HtmlFragments.PageHeader("Failed Jobs", subtitle) +
                HtmlFragments.FilterBar(PathPrefix, currentStatus, Search, null, null, queues) +
                HtmlFragments.EmptyState("12 22s10-9 10-9-9-9-9 9 10 9z", emptyMsg + " — " + emptySub) +
                "</div>";
            return HtmlShell.Wrap(Title, PathPrefix, "failed", emptyBody, Counters);
        }

        var headerActions =
            $"<form method=\"post\" action=\"{PathPrefix}/jobs/bulk\" style=\"display:inline\">" +
            $"<input type=\"hidden\" name=\"status\" value=\"{currentStatus}\" />" +
            "<button type=\"submit\" name=\"bulkAction\" value=\"requeue\" class=\"btn btn-primary\">↺ Requeue All</button></form> " +
            $"<form method=\"post\" action=\"{PathPrefix}/jobs/bulk\" style=\"display:inline\">" +
            $"<input type=\"hidden\" name=\"status\" value=\"{currentStatus}\" />" +
            $"<button type=\"submit\" name=\"bulkAction\" value=\"delete\" class=\"btn btn-danger\" onclick=\"return confirm('Delete all {currentStatus.ToLower()} jobs?')\">✕ Delete All</button></form>";

        var rows = string.Join(string.Empty, result.Items.Select(j => HtmlFragments.JobRowFailed(j, PathPrefix, now)));
        var baseUrl = $"{PathPrefix}/failed?search={Uri.EscapeDataString(Search ?? string.Empty)}";
        var pagination = HtmlFragments.Pagination(result, baseUrl);

        var body =
            "<div id=\"failed-page-content\" data-refresh=\"true\">" +
            HtmlFragments.Breadcrumbs(PathPrefix, ("Failed", null)) +
            HtmlFragments.PageHeader("Failed Jobs", subtitle, headerActions) +
            HtmlFragments.FilterBar(PathPrefix, currentStatus, Search, null, null, queues) +
            $"<div class=\"card\">" +
            $"<div class=\"card-header\"><h3>{result.TotalCount} jobs need attention</h3></div>" +
            $"<div style=\"padding:24px\">" +
            HtmlFragments.FailedStatusPills(currentStatus, PathPrefix + "/failed") +
            $"<div class=\"job-list\" style=\"display:flex;flex-direction:column;gap:8px\">{rows}</div>" +
            pagination +
            $"</div>" +
            $"</div>" +
            "</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "failed", body, Counters);
    }
}
