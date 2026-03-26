using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

internal sealed class RecurringPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IStorageProvider Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/jobs";
    [Parameter] public string Title { get; set; } = "NexJob";

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

        var rows = string.Join(string.Empty, jobs.Select(r =>
        {
            var countdown = r.NextExecution.HasValue && r.Enabled && !r.DeletedByUser
                ? Helpers.FormatCountdown(r.NextExecution.Value - now)
                : "<span style=\"color:var(--text-muted)\">—</span>";

            string lastRun;
            if (r.LastExecutedAt.HasValue)
            {
                var badge = r.LastExecutionStatus switch
                {
                    JobStatus.Succeeded => "<span class=\"badge badge-succeeded\" style=\"margin-left:4px\">✓ ok</span>",
                    JobStatus.Failed    => $"<span class=\"badge badge-failed\" title=\"{System.Web.HttpUtility.HtmlAttributeEncode(r.LastExecutionError ?? string.Empty)}\" style=\"margin-left:4px\">✗ err</span>",
                    _                  => string.Empty,
                };
                lastRun = $"<span title=\"{r.LastExecutedAt.Value:yyyy-MM-dd HH:mm:ss UTC}\">{r.LastExecutedAt.Value:MM/dd HH:mm}</span>{badge}";
            }
            else
            {
                lastRun = "<span style=\"color:var(--text-muted)\">never</span>";
            }

            var concurrencyBadge = r.ConcurrencyPolicy == RecurringConcurrencyPolicy.AllowConcurrent
                ? "<span class=\"badge\" style=\"background:var(--info,#4a9eff);color:#fff;margin-left:4px\" title=\"AllowConcurrent: multiple instances may run in parallel\">⟳ concurrent</span>"
                : string.Empty;

            var deletedBadge = r.DeletedByUser
                ? "<span class=\"badge badge-failed\" style=\"margin-left:4px\">Deleted</span>"
                : string.Empty;

            var pausedBadge = !r.DeletedByUser && !r.Enabled
                ? "<span class=\"badge\" style=\"background:var(--warning,#f59e0b);color:#000;margin-left:4px\">Paused</span>"
                : string.Empty;

            var effectiveCron = r.CronOverride ?? r.Cron;
            var cronOverrideBadge = r.CronOverride is not null
                ? $"<span class=\"badge\" style=\"background:var(--info,#4a9eff);color:#fff;margin-left:4px\" title=\"Override active; default: {System.Web.HttpUtility.HtmlAttributeEncode(r.Cron)}\">overridden</span>"
                : string.Empty;

            var encodedId = System.Web.HttpUtility.HtmlAttributeEncode(r.RecurringJobId);
            var encodedIdUrl = Uri.EscapeDataString(r.RecurringJobId);

            string actionsCell;
            if (r.DeletedByUser)
            {
                // Soft-deleted job: show only the Restore button
                var restoreButton =
                    $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/restore\" style=\"display:inline\"><button type=\"submit\" class=\"btn btn-primary btn-sm\" title=\"Restore this job\">↩ Restore</button></form>";

                actionsCell =
                    $"<div style=\"display:flex;gap:4px;flex-wrap:wrap;align-items:center\">" +
                    restoreButton +
                    $"</div>";
            }
            else
            {
                // Active job: show Trigger, Pause/Resume, Force Delete and Edit cron
                var pauseResumeButton = r.Enabled
                    ? $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/pause\" style=\"display:inline\"><button type=\"submit\" class=\"btn btn-sm\" style=\"background:var(--warning,#f59e0b);color:#000\" title=\"Pause\">⏸ Pause</button></form>"
                    : $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/resume\" style=\"display:inline\"><button type=\"submit\" class=\"btn btn-primary btn-sm\" title=\"Resume\">▶ Resume</button></form>";

                var editForm =
                    $"<details style=\"margin-top:4px\">" +
                    $"<summary class=\"btn btn-sm\" style=\"cursor:pointer;display:inline-block\">✎ Edit cron</summary>" +
                    $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/update-config\" style=\"margin-top:6px;display:flex;gap:6px;align-items:center\">" +
                    $"<input type=\"text\" name=\"cronOverride\" placeholder=\"{System.Web.HttpUtility.HtmlAttributeEncode(effectiveCron)}\" value=\"{System.Web.HttpUtility.HtmlAttributeEncode(r.CronOverride ?? string.Empty)}\" style=\"font-family:monospace;width:160px\" />" +
                    $"<button type=\"submit\" class=\"btn btn-primary btn-sm\">Save</button>" +
                    $"<button type=\"submit\" name=\"cronOverride\" value=\"\" class=\"btn btn-sm\">Reset to default</button>" +
                    $"</form>" +
                    $"</details>";

                var triggerButton =
                    $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/trigger\" style=\"display:inline\"><button type=\"submit\" class=\"btn btn-primary btn-sm\">▶ Trigger</button></form>";

                var forceDeleteButton =
                    $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/force-delete\" style=\"display:inline\"><button type=\"submit\" class=\"btn btn-danger btn-sm\" onclick=\"return confirm('Delete this job and all its records?')\">✕ Force Delete</button></form>";

                actionsCell =
                    $"<div style=\"display:flex;gap:4px;flex-wrap:wrap;align-items:center\">" +
                    triggerButton +
                    pauseResumeButton +
                    forceDeleteButton +
                    $"</div>" +
                    editForm;
            }

            return $"<tr>" +
                   $"<td style=\"width:36px\"><input type=\"checkbox\" name=\"ids\" value=\"{encodedId}\" /></td>" +
                   $"<td>{System.Web.HttpUtility.HtmlEncode(r.RecurringJobId)}{deletedBadge}{pausedBadge}</td>" +
                   $"<td><code style=\"color:var(--warning)\">{System.Web.HttpUtility.HtmlEncode(effectiveCron)}</code>{cronOverrideBadge}</td>" +
                   $"<td>{System.Web.HttpUtility.HtmlEncode(r.Queue)}</td>" +
                   $"<td>{Helpers.ShortType(r.JobType)}{concurrencyBadge}</td>" +
                   $"<td>{lastRun}</td>" +
                   $"<td>{countdown}</td>" +
                   $"<td>{actionsCell}</td>" +
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
            "<th>ID</th><th>Cron</th><th>Queue</th><th>Job</th><th>Last Execution</th><th>Next Execution</th><th>Actions</th>" +
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
