using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

internal sealed class FailedPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IStorageProvider Storage { get; set; } = default!;
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

        // Default to Failed if no status specified; allow Expired as override
        var status = StatusFilter ?? JobStatus.Failed;
        var filter = new JobFilter { Status = status, Search = Search };
        var result = await Storage.GetJobsAsync(filter, Page, 25);
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(result, status)));
    }

    private string BuildHtml(PagedResult<JobRecord> result, JobStatus status)
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
            var emptyState =
                $"<div class=\"inbox-zero\">" +
                $"<div class=\"inbox-zero-icon\">✓</div>" +
                $"<div class=\"inbox-zero-title\">{emptyMsg}</div>" +
                $"<div class=\"inbox-zero-sub\">{emptySub}</div>" +
                $"</div>";
            var emptyBody =
                "<div id=\"failed-page-content\" data-refresh=\"true\">" +
                HtmlFragments.PageHeader("Failed Jobs", subtitle) +
                HtmlFragments.FilterBar(PathPrefix, currentStatus, Search, null) +
                emptyState +
                "</div>";
            return HtmlShell.Wrap(Title, PathPrefix, "failed", emptyBody, Counters);
        }

        var jobPlural = result.TotalCount == 1 ? string.Empty : "s";
        var countBadge = $"<span class=\"inbox-count\">{result.TotalCount} need attention</span>";

        var banner = result.TotalCount > 25
            ? $"<div class=\"alert alert-danger\">⚠ {result.TotalCount} {currentStatus.ToLower()} job{jobPlural} — review and requeue or delete</div>"
            : string.Empty;

        var headerActions =
            $"<form method=\"post\" action=\"{PathPrefix}/jobs/bulk\" style=\"display:inline\">" +
            "<button type=\"submit\" name=\"bulkAction\" value=\"requeue\" class=\"btn btn-primary btn-sm\">↺ Requeue All</button></form> " +
            $"<form method=\"post\" action=\"{PathPrefix}/jobs/bulk\" style=\"display:inline\">" +
            "<button type=\"submit\" name=\"bulkAction\" value=\"delete\" class=\"btn btn-danger btn-sm\" onclick=\"return confirm('Delete all {0} jobs?')\">✕ Delete All</button></form>";

        var rows = string.Join(string.Empty, result.Items.Select(j => HtmlFragments.JobRowFailed(j, PathPrefix, now)));
        var baseUrl = $"{PathPrefix}/failed?search={Uri.EscapeDataString(Search ?? string.Empty)}";
        var pagination = HtmlFragments.Pagination(result, baseUrl);

        // Build filter section with status pills and search
        var searchVal = System.Web.HttpUtility.HtmlAttributeEncode(Search ?? string.Empty);
        var clearLink = searchVal.Length > 0
            ? $"<a href=\"{PathPrefix}/failed?status={Uri.EscapeDataString(currentStatus)}\" class=\"btn btn-ghost btn-sm\">Clear</a>"
            : string.Empty;
        var filterSection =
            $"<div class=\"filters\">" +
            $"<form method=\"get\" action=\"{PathPrefix}/failed\" style=\"display:contents\">" +
            $"<input type=\"text\" name=\"search\" placeholder=\"Search type or ID…\" value=\"{searchVal}\" />" +
            $"<input type=\"hidden\" name=\"status\" value=\"{System.Web.HttpUtility.HtmlAttributeEncode(currentStatus)}\" />" +
            $"<button type=\"submit\" class=\"btn btn-ghost btn-sm\">Search</button>" +
            clearLink +
            $"</form>" +
            HtmlFragments.FailedStatusPills(currentStatus, PathPrefix + "/failed") +
            $"</div>";

        var headerWithBadge =
            $"<div class=\"inbox-header\">" +
            $"<div><h1 class=\"page-title\">Failed Jobs</h1><p class=\"page-subtitle\">{System.Web.HttpUtility.HtmlEncode(subtitle)}</p></div>" +
            $"<div style=\"display:flex;gap:8px;align-items:center\">" +
            countBadge +
            $"<div style=\"display:flex;gap:6px\">{string.Format(headerActions, currentStatus.ToLower())}</div>" +
            $"</div>" +
            $"</div>";

        var body =
            "<div id=\"failed-page-content\" data-refresh=\"true\">" +
            headerWithBadge +
            banner +
            filterSection +
            "<div class=\"section\">" +
            $"<div class=\"job-list\" style=\"display:flex;flex-direction:column;gap:8px\">{rows}</div>" +
            pagination +
            "</div>" +
            "</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "failed", body, Counters);
    }
}
