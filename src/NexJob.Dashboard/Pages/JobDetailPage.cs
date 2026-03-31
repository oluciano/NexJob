using System.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

internal sealed class JobDetailPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IStorageProvider Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/dashboard";
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
                HtmlFragments.EmptyState("0 0 24 24", "Job not found") +
                $"<div style=\"text-align:center;margin-top:12px\"><a href=\"{PathPrefix}/jobs\" class=\"btn btn-ghost btn-sm\">← Back to Jobs</a></div>";
            return HtmlShell.Wrap(Title, PathPrefix, "jobs", notFoundHtml);
        }

        var now = DateTimeOffset.UtcNow;
        var vm = new JobDetailViewModel { Job = job, PathPrefix = PathPrefix, Now = now };

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

        // Tags HTML
        var tagsHtml = job.Tags.Count > 0
            ? string.Join(" ", job.Tags.Select(t =>
                $"<span class=\"tag-badge\">{HttpUtility.HtmlEncode(t)}</span>"))
            : "—";

        // Header
        var header =
            $"<div style=\"display:flex;align-items:flex-start;justify-content:space-between;gap:16px;margin-bottom:8px;flex-wrap:wrap\">" +
            $"<div>" +
            $"<a href=\"{PathPrefix}/jobs\" style=\"font-size:12px;color:var(--text-3)\">← Jobs</a>" +
            $"<h1 class=\"page-title\" style=\"margin-top:6px;margin-bottom:2px\">{HttpUtility.HtmlEncode(vm.ShortType)}</h1>" +
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
        var progressSection = HtmlFragments.ProgressBar(job.ProgressPercent, job.ProgressMessage);

        // Detail sections
        var timelineRows = new List<(string Label, string Value)>
        {
            ("Created", $"{job.CreatedAt:yyyy-MM-dd HH:mm:ss UTC} <span style=\"color:var(--text-3);font-size:11px\">({Helpers.RelativeTime(job.CreatedAt, now)})</span>"),
            ("Scheduled", job.ScheduledAt.HasValue ? $"{job.ScheduledAt.Value:yyyy-MM-dd HH:mm:ss UTC} <span style=\"color:var(--text-3);font-size:11px\">({Helpers.CountdownFriendly(job.ScheduledAt.Value - now)})</span>" : "—"),
            ("Started", job.ProcessingStartedAt.HasValue ? $"{job.ProcessingStartedAt.Value:yyyy-MM-dd HH:mm:ss UTC}" : "—"),
            ("Completed", job.CompletedAt.HasValue ? $"{job.CompletedAt.Value:yyyy-MM-dd HH:mm:ss UTC}" : "—"),
        };

        // Add expiration info only if deadline exists
        if (job.ExpiresAt.HasValue)
        {
            var expirationColor = job.Status == JobStatus.Expired
                ? "color:var(--danger)"
                : string.Empty;

            if (string.IsNullOrEmpty(expirationColor) && now >= job.ExpiresAt.Value)
            {
                expirationColor = "color:var(--warning)";
            }

            timelineRows.Add(("Expires At", $"<span style=\"{expirationColor}\">{job.ExpiresAt.Value:yyyy-MM-dd HH:mm:ss UTC}</span>"));
        }

        timelineRows.Add(("Retry At", job.RetryAt.HasValue ? $"{job.RetryAt.Value:yyyy-MM-dd HH:mm:ss UTC}" : "—"));

        var timeline = HtmlFragments.DetailSection("Timeline", timelineRows.ToArray());

        var configuration =
            HtmlFragments.DetailSection("Configuration",
                ("Queue", HttpUtility.HtmlEncode(job.Queue)),
                ("Priority", job.Priority.ToString()),
                ("Max Attempts", job.MaxAttempts.ToString()),
                ("Idempotency", job.IdempotencyKey is null ? "—" : HttpUtility.HtmlEncode(job.IdempotencyKey)),
                ("Tags", tagsHtml));

        var relationships =
            HtmlFragments.DetailSection("Relationships",
                ("Parent Job", job.ParentJobId.HasValue ? $"<a href=\"{PathPrefix}/jobs/{job.ParentJobId.Value.Value}\">{job.ParentJobId.Value.Value}</a>" : "—"),
                ("Recurring", job.RecurringJobId is not null ? $"<a href=\"{PathPrefix}/recurring/{Uri.EscapeDataString(job.RecurringJobId)}\">{HttpUtility.HtmlEncode(job.RecurringJobId)}</a>" : "—"));

        // Payload
        var payloadSection =
            "<div style=\"margin-bottom:24px\">" +
            "<div class=\"section-title\" style=\"margin-bottom:8px\">Payload</div>" +
            $"<pre>{Helpers.FormatJson(job.InputJson)}</pre>" +
            "</div>";

        // Error section
        var errorSection = HtmlFragments.ErrorSection(job.LastErrorMessage, job.LastErrorStackTrace);

        // Logs section
        var logsSection = HtmlFragments.LogsSection(job.ExecutionLogs);

        // SSE for live progress
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
