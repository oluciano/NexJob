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

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        var filter = new JobFilter { Status = JobStatus.Failed };
        var result = await Storage.GetJobsAsync(filter, 1, 50);
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(result)));
    }

    private string BuildHtml(PagedResult<JobRecord> result)
    {
        var now = DateTimeOffset.UtcNow;

        // Banner for large dead-letter queues
        var banner = result.TotalCount > 50
            ? $"<div class=\"alert alert-danger\">⚠ {result.TotalCount} dead-letter jobs — review and requeue or delete</div>"
            : string.Empty;

        // Header actions
        var headerActions =
            $"<div class=\"page-header-actions\">" +
            $"<form method=\"post\" action=\"{PathPrefix}/jobs/bulk\" style=\"display:inline\">" +
            "<button type=\"submit\" name=\"bulkAction\" value=\"requeue\" class=\"btn btn-primary btn-sm\">↺ Requeue All</button></form> " +
            $"<form method=\"post\" action=\"{PathPrefix}/jobs/bulk\" style=\"display:inline\">" +
            "<button type=\"submit\" name=\"bulkAction\" value=\"delete\" class=\"btn btn-danger btn-sm\" onclick=\"return confirm('Delete all failed jobs?')\">✕ Delete All</button></form>" +
            $"</div>";

        if (result.Items.Count == 0)
        {
            var emptyBody =
                "<div id=\"failed-page-content\" data-refresh=\"true\">" +
                "<div class=\"page-header\"><div><h1 class=\"page-title\">Failed Jobs</h1><p class=\"page-subtitle\">Dead-letter queue</p></div></div>" +
                "<div class=\"empty-state\"><svg width=\"40\" height=\"40\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1\"><circle cx=\"12\" cy=\"12\" r=\"9\"/><line x1=\"9\" y1=\"9\" x2=\"15\" y2=\"15\"/><line x1=\"15\" y1=\"9\" x2=\"9\" y2=\"15\"/></svg><p>No failed jobs. Looking good.</p></div>" +
                "</div>";
            return HtmlShell.Wrap(Title, PathPrefix, "failed", emptyBody);
        }

        var rows = string.Join(string.Empty, result.Items.Select(j =>
        {
            var errorSnippet = Helpers.Truncate(j.LastErrorMessage, 90);

            return
                $"<a href=\"{PathPrefix}/jobs/{j.Id.Value}\" style=\"text-decoration:none\">" +
                $"<div class=\"job-row\">" +
                $"<div class=\"job-row-dot\">{Helpers.StatusDot(JobStatus.Failed)}</div>" +
                $"<div class=\"job-row-main\">" +
                $"<div class=\"job-row-title\">{System.Web.HttpUtility.HtmlEncode(Helpers.ShortType(j.JobType))}</div>" +
                $"<div class=\"job-row-sub\">" +
                $"<span style=\"font-family:monospace;font-size:11px;color:var(--text-3)\">{j.Id.Value.ToString()[..8]}…</span>" +
                $"<span>{System.Web.HttpUtility.HtmlEncode(j.Queue)}</span>" +
                $"<span>attempt {j.Attempts}/{j.MaxAttempts}</span>" +
                $"</div>" +
                $"<div style=\"font-size:12px;color:var(--danger);margin-top:4px\">{System.Web.HttpUtility.HtmlEncode(errorSnippet)}</div>" +
                $"</div>" +
                $"<div class=\"job-row-meta\">" +
                $"{Helpers.RelativeTime(j.CompletedAt, now)}" +
                $"</div>" +
                $"</div></a>";
        }));

        var body =
            "<div id=\"failed-page-content\" data-refresh=\"true\">" +
            "<div class=\"page-header\">" +
            "<div>" +
            "<h1 class=\"page-title\">Failed Jobs</h1>" +
            $"<p class=\"page-subtitle\" style=\"color:var(--danger)\">{result.TotalCount} dead-letter job{(result.TotalCount == 1 ? string.Empty : "s")}</p>" +
            "</div>" +
            headerActions +
            "</div>" +
            banner +
            "<div class=\"section\">" +
            "<div class=\"job-list\">" + rows + "</div>" +
            "</div>" +
            "</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "failed", body);
    }
}
