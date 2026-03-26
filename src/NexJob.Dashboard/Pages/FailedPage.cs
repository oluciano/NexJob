using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

internal sealed class FailedPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IStorageProvider Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/jobs";
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
        var rows = string.Join(string.Empty, result.Items.Select(j =>
            $"<tr>" +
            $"<td style=\"width:36px\"><input type=\"checkbox\" name=\"ids\" value=\"{j.Id.Value}\" /></td>" +
            $"<td><a href=\"{PathPrefix}/jobs/{j.Id.Value}\">{j.Id.Value.ToString()[..8]}…</a></td>" +
            $"<td>{Helpers.ShortType(j.JobType)}</td>" +
            $"<td>{System.Web.HttpUtility.HtmlEncode(j.Queue)}</td>" +
            $"<td>{j.Attempts}</td>" +
            $"<td>{j.CompletedAt?.ToString("MM/dd HH:mm") ?? "—"}</td>" +
            $"<td style=\"color:var(--danger)\">{Helpers.Truncate(j.LastErrorMessage, 72)}</td>" +
            $"</tr>"));

        var table =
            $"<form method=\"post\" action=\"{PathPrefix}/jobs/bulk\" id=\"bulk-form\">" +
            "<div class=\"filters\" style=\"margin-bottom:16px\">" +
            "<button type=\"submit\" name=\"bulkAction\" value=\"requeue\" class=\"btn btn-primary btn-sm\">↺ Requeue Selected</button>" +
            "<button type=\"submit\" name=\"bulkAction\" value=\"delete\" class=\"btn btn-danger btn-sm\" onclick=\"return confirm('Delete selected?')\">✕ Delete Selected</button>" +
            "<span style=\"color:var(--text-muted);font-size:12px;margin-left:8px\">Select rows, or act on all if none selected.</span>" +
            "</div>" +
            "<table><thead><tr>" +
            "<th style=\"width:36px\"><input type=\"checkbox\" id=\"select-all\" title=\"Select all\" /></th>" +
            "<th>ID</th><th>Type</th><th>Queue</th><th>Attempts</th><th>Failed At</th><th>Error</th>" +
            $"</tr></thead><tbody>{rows}</tbody></table>" +
            "</form>" +
            "<script>" +
            "document.getElementById('select-all').addEventListener('change',function(){" +
            "document.querySelectorAll('input[name=\"ids\"]').forEach(c=>c.checked=this.checked);});" +
            "</script>";

        var body =
            "<h1 class=\"page-title\">Failed Jobs</h1>" +
            "<div class=\"section\">" +
            (result.Items.Count == 0
                ? "<p style=\"color:var(--text-muted)\">No failed jobs. 🎉</p>"
                : $"<p style=\"color:var(--text-muted);font-size:12px;margin-bottom:16px\">{result.TotalCount} failed job(s)</p>" +
                  table) +
            "</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "failed", body);
    }
}
