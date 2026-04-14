using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

internal sealed class RecurringPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IRecurringStorage Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/dashboard";
    [Parameter] public string Title { get; set; } = "NexJob";
    [Parameter] public NavCounters? Counters { get; set; }

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        var jobs = await Storage.GetRecurringJobsAsync().ConfigureAwait(false);
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(jobs)));
    }

    private string BuildHtml(IReadOnlyList<RecurringJobRecord> jobs)
    {
        var now = DateTimeOffset.UtcNow;

        if (jobs.Count == 0)
        {
            var emptyBody =
                "<div class=\"page-header\"><div><h1 class=\"page-title\">Recurring Jobs</h1><p class=\"page-subtitle\">Scheduled cron jobs</p></div></div>" +
                "<div class=\"empty-state\"><svg width=\"40\" height=\"40\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1\"><path d=\"M21 8A9 9 0 1 1 12 3v4l-3-3\"/><polyline points=\"15,3 12,6 15,9\"/></svg><p>No recurring jobs registered.</p></div>";
            return HtmlShell.Wrap(Title, PathPrefix, "recurring", emptyBody, Counters);
        }

        var cards = string.Join(string.Empty, jobs.Select(r =>
        {
            var effectiveCron = r.CronOverride ?? r.Cron;
            var encodedIdUrl = Uri.EscapeDataString(r.RecurringJobId);

            // Countdown / next execution
            string nextHtml;
            if (r.DeletedByUser)
            {
                nextHtml = "<span style=\"color:var(--text-3)\">deleted</span>";
            }
            else if (!r.Enabled)
            {
                nextHtml = "<span style=\"color:var(--warning)\">paused</span>";
            }
            else if (r.NextExecution.HasValue)
            {
                nextHtml = $"<span style=\"color:var(--accent-light)\">{Helpers.CountdownFriendly(r.NextExecution.Value - now)}</span>";
            }
            else
            {
                nextHtml = "<span style=\"color:var(--text-3)\">—</span>";
            }

            // Last execution status badge
            string lastRunHtml;
            if (r.LastExecutedAt.HasValue)
            {
                var statusColor = r.LastExecutionStatus switch
                {
                    JobStatus.Succeeded => "var(--success)",
                    JobStatus.Failed => "var(--danger)",
                    _ => "var(--text-3)",
                };
                var statusIcon = r.LastExecutionStatus == JobStatus.Failed ? "✗" : "✓";
                var title = r.LastExecutionStatus == JobStatus.Failed && r.LastExecutionError is not null
                    ? $" title=\"{System.Web.HttpUtility.HtmlAttributeEncode(r.LastExecutionError)}\""
                    : string.Empty;
                lastRunHtml = $"<span style=\"color:{statusColor};font-size:12px\"{title}>{statusIcon} {Helpers.RelativeTime(r.LastExecutedAt, now)}</span>";
            }
            else
            {
                lastRunHtml = "<span style=\"color:var(--text-3);font-size:12px\">never run</span>";
            }

            // State badges
            var stateBadges = string.Empty;
            if (r.DeletedByUser)
            {
                stateBadges += " <span class=\"badge badge-deleted\">Deleted</span>";
            }
            else if (!r.Enabled)
            {
                stateBadges += " <span class=\"badge badge-processing\">Paused</span>";
            }

            if (r.ConcurrencyPolicy == RecurringConcurrencyPolicy.AllowConcurrent)
            {
                stateBadges += " <span class=\"badge badge-scheduled\">⟳ concurrent</span>";
            }

            if (r.CronOverride is not null)
            {
                stateBadges += $" <span class=\"badge badge-awaiting\" title=\"Default: {System.Web.HttpUtility.HtmlAttributeEncode(r.Cron)}\">cron overridden</span>";
            }

            // Action buttons
            string actionsHtml;
            if (r.DeletedByUser)
            {
                actionsHtml =
                    $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/restore\" style=\"display:inline\">" +
                    "<button type=\"submit\" class=\"btn btn-ghost btn-sm\">↩ Restore</button></form>";
            }
            else
            {
                var pauseResume = r.Enabled
                    ? $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/pause\" style=\"display:inline\"><button type=\"submit\" class=\"btn btn-ghost btn-sm\">⏸ Pause</button></form>"
                    : $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/resume\" style=\"display:inline\"><button type=\"submit\" class=\"btn btn-primary btn-sm\">▶ Resume</button></form>";

                actionsHtml =
                    $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/trigger\" style=\"display:inline\">" +
                    "<button type=\"submit\" class=\"btn btn-primary btn-sm\">▶ Trigger</button></form> " +
                    pauseResume + " " +
                    $"<a href=\"{PathPrefix}/recurring/{encodedIdUrl}\" class=\"btn btn-ghost btn-sm\">Details</a>";
            }

            return
                $"<div class=\"recurring-card\">" +
                $"<div class=\"recurring-card-header\">" +
                $"<div class=\"recurring-card-left\">" +
                $"<span class=\"recurring-id\">{System.Web.HttpUtility.HtmlEncode(r.RecurringJobId)}</span>" +
                stateBadges +
                $"</div>" +
                $"<div class=\"recurring-card-right\">" +
                lastRunHtml +
                $"</div>" +
                $"</div>" +
                $"<div class=\"recurring-card-meta\">" +
                $"<span>{System.Web.HttpUtility.HtmlEncode(Helpers.ShortType(r.JobType))}</span>" +
                $"<code class=\"cron\">{System.Web.HttpUtility.HtmlEncode(effectiveCron)}</code>" +
                $"<span>{System.Web.HttpUtility.HtmlEncode(r.Queue)}</span>" +
                $"<span>Next: {nextHtml}</span>" +
                $"</div>" +
                $"<div style=\"margin-top:10px;display:flex;gap:6px;flex-wrap:wrap\">{actionsHtml}</div>" +
                $"</div>";
        }));

        var body =
            "<div id=\"recurring-page-content\" data-refresh=\"true\">" +
            "<div class=\"page-header\"><div>" +
            "<h1 class=\"page-title\">Recurring Jobs</h1>" +
            $"<p class=\"page-subtitle\">{jobs.Count} job{(jobs.Count == 1 ? string.Empty : "s")} registered</p>" +
            "</div></div>" +
            "<div class=\"recurring-list\">" + cards + "</div>" +
            "</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "recurring", body, Counters);
    }
}
