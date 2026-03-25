using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

internal sealed class RecurringPage : IComponent
{
    [Parameter] public IStorageProvider Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/jobs";
    [Parameter] public string Title { get; set; } = "NexJob";

    private RenderHandle _handle;

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        var jobs = await Storage.GetRecurringJobsAsync();
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(jobs)));
    }

    private string BuildHtml(IReadOnlyList<RecurringJobRecord> jobs)
    {
        var now = DateTimeOffset.UtcNow;

        var rows = string.Join("", jobs.Select(r =>
        {
            var countdown = r.NextExecution.HasValue
                ? Helpers.FormatCountdown(r.NextExecution.Value - now)
                : "<span style=\"color:var(--text-muted)\">—</span>";

            string lastRun;
            if (r.LastExecutedAt.HasValue)
            {
                var badge = r.LastExecutionStatus switch
                {
                    JobStatus.Succeeded => "<span class=\"badge badge-succeeded\" style=\"margin-left:4px\">✓ ok</span>",
                    JobStatus.Failed    => $"<span class=\"badge badge-failed\" title=\"{System.Web.HttpUtility.HtmlAttributeEncode(r.LastExecutionError ?? "")}\" style=\"margin-left:4px\">✗ err</span>",
                    _                  => "",
                };
                lastRun = $"<span title=\"{r.LastExecutedAt.Value:yyyy-MM-dd HH:mm:ss UTC}\">{r.LastExecutedAt.Value:MM/dd HH:mm}</span>{badge}";
            }
            else
            {
                lastRun = "<span style=\"color:var(--text-muted)\">never</span>";
            }

            return $"<tr>" +
                   $"<td style=\"width:36px\"><input type=\"checkbox\" name=\"ids\" value=\"{System.Web.HttpUtility.HtmlAttributeEncode(r.RecurringJobId)}\" /></td>" +
                   $"<td>{System.Web.HttpUtility.HtmlEncode(r.RecurringJobId)}</td>" +
                   $"<td><code style=\"color:var(--warning)\">{System.Web.HttpUtility.HtmlEncode(r.Cron)}</code></td>" +
                   $"<td>{System.Web.HttpUtility.HtmlEncode(r.Queue)}</td>" +
                   $"<td>{Helpers.ShortType(r.JobType)}</td>" +
                   $"<td>{lastRun}</td>" +
                   $"<td>{countdown}</td>" +
                   $"</tr>";
        }));

        var table =
            $"<form method=\"post\" action=\"{PathPrefix}/recurring/bulk\" id=\"bulk-form\">" +
            "<div class=\"filters\" style=\"margin-bottom:16px\">" +
            "<button type=\"submit\" name=\"bulkAction\" value=\"trigger\" class=\"btn btn-primary btn-sm\">▶ Trigger Now</button>" +
            "<button type=\"submit\" name=\"bulkAction\" value=\"delete\" class=\"btn btn-danger btn-sm\" onclick=\"return confirm('Delete selected?')\">✕ Delete</button>" +
            "<span style=\"color:var(--text-muted);font-size:12px;margin-left:8px\">Select rows below, or trigger/delete all if none selected.</span>" +
            "</div>" +
            "<table><thead><tr>" +
            "<th style=\"width:36px\"><input type=\"checkbox\" id=\"select-all\" title=\"Select all\" /></th>" +
            "<th>ID</th><th>Cron</th><th>Queue</th><th>Job</th><th>Last Execution</th><th>Next Execution</th>" +
            $"</tr></thead><tbody>{rows}</tbody></table>" +
            "</form>" +
            "<script>" +
            "document.getElementById('select-all').addEventListener('change',function(){" +
            "document.querySelectorAll('input[name=\"ids\"]').forEach(c=>c.checked=this.checked);});" +
            "</script>";

        var body =
            "<h1 class=\"page-title\">Recurring Jobs</h1>" +
            $"<p style=\"color:var(--text-muted);font-size:12px;margin-bottom:16px\">{jobs.Count} job(s) registered</p>" +
            "<div class=\"section\">" +
            (jobs.Count == 0
                ? "<p style=\"color:var(--text-muted)\">No recurring jobs registered.</p>"
                : table) +
            "</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "recurring", body);
    }
}
