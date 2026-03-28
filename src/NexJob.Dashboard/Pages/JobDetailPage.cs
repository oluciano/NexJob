using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

internal sealed class JobDetailPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IStorageProvider Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/jobs";
    [Parameter] public string Title { get; set; } = "NexJob";
    [Parameter] public JobId JobId { get; set; }

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        var job = await Storage.GetJobByIdAsync(JobId);
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(job)));
    }

    private string BuildHtml(JobRecord? job)
    {
        if (job is null)
        {
            var notFoundHtml =
                "<div class=\"empty-state\"><svg width=\"40\" height=\"40\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1\"><circle cx=\"12\" cy=\"12\" r=\"9\"/><line x1=\"12\" y1=\"8\" x2=\"12\" y2=\"12\"/><line x1=\"12\" y1=\"16\" x2=\"12.01\" y2=\"16\"/></svg><p>Job not found</p></div>" +
                $"<div style=\"text-align:center;margin-top:12px\"><a href=\"{PathPrefix}/jobs\" class=\"btn btn-ghost btn-sm\">← Back to Jobs</a></div>";
            return HtmlShell.Wrap(Title, PathPrefix, "jobs", notFoundHtml);
        }

        var now = DateTimeOffset.UtcNow;

        // Action buttons
        var actions = string.Empty;
        if (job.Status == JobStatus.Scheduled)
        {
            actions +=
                $"<form method=\"post\" action=\"{PathPrefix}/jobs/{job.Id.Value}/runnow\" style=\"display:inline\">" +
                "<button type=\"submit\" class=\"btn btn-primary btn-sm\">▶ Run Now</button></form> ";
        }

        if (job.Status == JobStatus.Failed)
        {
            actions +=
                $"<form method=\"post\" action=\"{PathPrefix}/jobs/{job.Id.Value}/requeue\" style=\"display:inline\">" +
                "<button type=\"submit\" class=\"btn btn-primary btn-sm\">↺ Requeue</button></form> " +
                $"<form method=\"post\" action=\"{PathPrefix}/jobs/{job.Id.Value}/delete\" style=\"display:inline\">" +
                "<button type=\"submit\" class=\"btn btn-danger btn-sm\" onclick=\"return confirm('Delete this job?')\">Delete</button></form>";
        }

        // Tags
        var tagsHtml = job.Tags.Count > 0
            ? string.Join(" ", job.Tags.Select(t =>
                $"<span class=\"tag-badge\">{System.Web.HttpUtility.HtmlEncode(t)}</span>"))
            : "—";

        // Header
        var header =
            $"<div style=\"display:flex;align-items:flex-start;justify-content:space-between;gap:16px;margin-bottom:8px;flex-wrap:wrap\">" +
            $"<div>" +
            $"<a href=\"{PathPrefix}/jobs\" style=\"font-size:12px;color:var(--text-3)\">← Jobs</a>" +
            $"<h1 class=\"page-title\" style=\"margin-top:6px;margin-bottom:2px\">{System.Web.HttpUtility.HtmlEncode(Helpers.ShortType(job.JobType))}</h1>" +
            $"<div style=\"font-family:monospace;font-size:12px;color:var(--text-3);margin-bottom:8px\">{job.Id.Value}</div>" +
            $"<div style=\"display:flex;align-items:center;gap:10px\">{Helpers.BadgeHtml(job.Status)}" +
            $"<span style=\"font-size:12px;color:var(--text-2)\">attempt {job.Attempts}/{job.MaxAttempts}</span></div>" +
            $"</div>" +
            (actions.Length > 0
                ? $"<div style=\"display:flex;gap:8px;align-items:center;flex-wrap:wrap\">{actions}</div>"
                : string.Empty) +
            $"</div>" +
            $"<div style=\"border-bottom:1px solid var(--border);margin-bottom:24px\"></div>";

        // Progress bar
        var progressSection = string.Empty;
        if (job.ProgressPercent.HasValue)
        {
            var pct = job.ProgressPercent.Value;
            var msgHtml = job.ProgressMessage is not null
                ? System.Web.HttpUtility.HtmlEncode(job.ProgressMessage)
                : string.Empty;
            progressSection =
                $"<div class=\"progress-wrap\">" +
                $"<div class=\"progress-bar-track\">" +
                $"<div id=\"progress-bar-fill\" class=\"progress-bar-fill\" style=\"width:{pct}%\"></div>" +
                $"</div>" +
                $"<div class=\"progress-info\">" +
                $"<span id=\"progress-pct\" class=\"progress-pct\">{pct}%</span>" +
                $"<span id=\"progress-msg\">{msgHtml}</span>" +
                $"</div>" +
                $"</div>";
        }

        // Detail sections — Timeline, Configuration, Relationships
        var timeline =
            "<div class=\"detail-section\">" +
            "<div class=\"detail-section-header\">Timeline</div>" +
            "<div class=\"detail-grid\">" +
            $"<div class=\"detail-label\">Created</div><div class=\"detail-value\">{job.CreatedAt:yyyy-MM-dd HH:mm:ss UTC} <span style=\"color:var(--text-3);font-size:11px\">({Helpers.RelativeTime(job.CreatedAt, now)})</span></div>" +
            $"<div class=\"detail-label\">Scheduled</div><div class=\"detail-value\">{(job.ScheduledAt.HasValue ? $"{job.ScheduledAt.Value:yyyy-MM-dd HH:mm:ss UTC} <span style=\"color:var(--text-3);font-size:11px\">({Helpers.CountdownFriendly(job.ScheduledAt.Value - now)})</span>" : "—")}</div>" +
            $"<div class=\"detail-label\">Started</div><div class=\"detail-value\">{(job.ProcessingStartedAt.HasValue ? $"{job.ProcessingStartedAt.Value:yyyy-MM-dd HH:mm:ss UTC}" : "—")}</div>" +
            $"<div class=\"detail-label\">Completed</div><div class=\"detail-value\">{(job.CompletedAt.HasValue ? $"{job.CompletedAt.Value:yyyy-MM-dd HH:mm:ss UTC}" : "—")}</div>" +
            $"<div class=\"detail-label\">Retry At</div><div class=\"detail-value\">{(job.RetryAt.HasValue ? $"{job.RetryAt.Value:yyyy-MM-dd HH:mm:ss UTC}" : "—")}</div>" +
            "</div></div>";

        var configuration =
            "<div class=\"detail-section\">" +
            "<div class=\"detail-section-header\">Configuration</div>" +
            "<div class=\"detail-grid\">" +
            $"<div class=\"detail-label\">Queue</div><div class=\"detail-value\">{System.Web.HttpUtility.HtmlEncode(job.Queue)}</div>" +
            $"<div class=\"detail-label\">Priority</div><div class=\"detail-value\">{job.Priority}</div>" +
            $"<div class=\"detail-label\">Max Attempts</div><div class=\"detail-value\">{job.MaxAttempts}</div>" +
            $"<div class=\"detail-label\">Idempotency</div><div class=\"detail-value\">{(job.IdempotencyKey is null ? "—" : System.Web.HttpUtility.HtmlEncode(job.IdempotencyKey))}</div>" +
            $"<div class=\"detail-label\">Tags</div><div class=\"detail-value\">{tagsHtml}</div>" +
            "</div></div>";

        var relationships =
            "<div class=\"detail-section\">" +
            "<div class=\"detail-section-header\">Relationships</div>" +
            "<div class=\"detail-grid\">" +
            $"<div class=\"detail-label\">Parent Job</div><div class=\"detail-value\">{(job.ParentJobId.HasValue ? $"<a href=\"{PathPrefix}/jobs/{job.ParentJobId.Value.Value}\">{job.ParentJobId.Value.Value}</a>" : "—")}</div>" +
            $"<div class=\"detail-label\">Recurring</div><div class=\"detail-value\">{(job.RecurringJobId is not null ? $"<a href=\"{PathPrefix}/recurring/{Uri.EscapeDataString(job.RecurringJobId)}\">{System.Web.HttpUtility.HtmlEncode(job.RecurringJobId)}</a>" : "—")}</div>" +
            "</div></div>";

        // Payload with JSON syntax highlight
        var payloadSection =
            "<div style=\"margin-bottom:24px\">" +
            "<div class=\"section-title\" style=\"margin-bottom:8px\">Payload</div>" +
            $"<pre>{Helpers.FormatJson(job.InputJson)}</pre>" +
            "</div>";

        // Error section
        string errorSection;
        if (job.LastErrorMessage is null)
        {
            errorSection = string.Empty;
        }
        else
        {
            var stackTrace = job.LastErrorStackTrace is null
                ? string.Empty
                : "<div class=\"section-title\" style=\"color:var(--danger);margin-bottom:8px;margin-top:16px\">Stack Trace</div>" +
                  $"<pre style=\"border-color:rgba(248,113,113,.2);background:#0e0808\">{System.Web.HttpUtility.HtmlEncode(job.LastErrorStackTrace)}</pre>";

            errorSection =
                "<div style=\"margin-bottom:24px\">" +
                "<div class=\"section-title\" style=\"color:var(--danger);margin-bottom:8px\">Last Error</div>" +
                $"<pre style=\"border-color:rgba(248,113,113,.2);background:#0e0808\">{System.Web.HttpUtility.HtmlEncode(job.LastErrorMessage)}</pre>" +
                stackTrace +
                "</div>";
        }

        // Execution logs — terminal style
        string logsSection;
        if (job.ExecutionLogs.Count == 0)
        {
            logsSection =
                "<div style=\"margin-bottom:24px\">" +
                "<div class=\"section-title\" style=\"margin-bottom:8px\">Execution Logs</div>" +
                "<p style=\"color:var(--text-3);font-size:13px\">No logs captured for this execution.</p>" +
                "</div>";
        }
        else
        {
            var logLines = string.Join(string.Empty, job.ExecutionLogs.Select(entry =>
            {
                var color = entry.Level switch
                {
                    "Warning" => "#fbbf24",
                    "Error" or "Critical" => "#f87171",
                    "Debug" or "Trace" => "#6b7280",
                    _ => "#e5e7eb",
                };
                var ts = entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var msg = System.Web.HttpUtility.HtmlEncode(entry.Message).Replace("\n", "&#10;");
                return $"<span style=\"color:{color}\">[{ts}] [{entry.Level,-11}] {msg}</span>\n";
            }));

            logsSection =
                "<div style=\"margin-bottom:24px\">" +
                $"<div class=\"section-title\" style=\"margin-bottom:8px\">Execution Logs " +
                $"<span style=\"color:var(--text-3);font-weight:400;text-transform:none;letter-spacing:0\">({job.ExecutionLogs.Count} entries)</span></div>" +
                $"<div class=\"log-terminal\">{logLines}</div>" +
                "</div>";
        }

        // SSE for live progress on active jobs
        var sseScript =
            $"<script>(function(){{" +
            $"var jobId='{job.Id.Value}';" +
            $"var fill=document.getElementById('progress-bar-fill');" +
            $"var pctEl=document.getElementById('progress-pct');" +
            $"var msgEl=document.getElementById('progress-msg');" +
            $"if(!fill)return;" +
            $"var es=new EventSource('{PathPrefix}/stream');" +
            $"es.onmessage=function(e){{" +
            $"var d=JSON.parse(e.data);" +
            $"var jobs=d.activeJobs||[];" +
            $"var j=jobs.find(function(x){{return x.id===jobId;}});" +
            $"if(j&&j.progressPercent!==null&&j.progressPercent!==undefined){{" +
            $"fill.style.width=j.progressPercent+'%';" +
            $"if(pctEl)pctEl.textContent=j.progressPercent+'%';" +
            $"if(msgEl&&j.progressMessage)msgEl.textContent=j.progressMessage;" +
            $"}}}};es.onerror=function(){{es.close();}};" +
            $"}})();</script>";

        var body =
            header +
            progressSection +
            "<div class=\"detail-sections\">" +
            timeline +
            configuration +
            relationships +
            "</div>" +
            payloadSection +
            errorSection +
            logsSection +
            sseScript;

        return HtmlShell.Wrap(Title, PathPrefix, "jobs", body);
    }
}
