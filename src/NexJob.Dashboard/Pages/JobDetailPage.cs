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
    [Parameter] public NavCounters? Counters { get; set; }
    [Parameter] public JobId JobId { get; set; }
    [Parameter] public bool IsReadOnly { get; set; }

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
            return HtmlShell.Wrap(Title, PathPrefix, "jobs", notFoundHtml, Counters);
        }

        var now = DateTimeOffset.UtcNow;
        var vm = new JobDetailViewModel { Job = job, PathPrefix = PathPrefix, Now = now };

        // Action buttons
        var actions = string.Empty;
        if (!IsReadOnly)
        {
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
                    "<button type=\"submit\" class=\"btn btn-primary btn-sm\" onclick=\"return confirm('Requeue this job?')\">↺ Requeue</button></form> " +
                    $"<form method=\"post\" action=\"{PathPrefix}/jobs/{job.Id.Value}/delete\" style=\"display:inline\">" +
                    "<button type=\"submit\" class=\"btn btn-danger btn-sm\" onclick=\"return confirm('Delete this job?')\">Delete</button></form>";
            }
        }

        // Tags HTML
        var tagsHtml = job.Tags.Count > 0
            ? string.Join(" ", job.Tags.Select(t =>
                $"<span class=\"tag-badge\">{HttpUtility.HtmlEncode(t)}</span>"))
            : "—";

        // Header
        var header =
            $"<div style=\"display:flex;align-items:flex-start;justify-content:space-between;gap:20px;margin-bottom:20px;flex-wrap:wrap\">" +
            $"<div style=\"flex:1\">" +
            $"<a href=\"{PathPrefix}/jobs\" style=\"font-size:12px;color:var(--text-3);display:inline-block;margin-bottom:8px\">← Back</a>" +
            $"<h1 class=\"page-title\" style=\"margin-bottom:4px\">{HttpUtility.HtmlEncode(vm.ShortType)}</h1>" +
            $"<div style=\"font-family:monospace;font-size:12px;color:var(--text-3);margin-bottom:12px;letter-spacing:0.02em\">{job.Id.Value}</div>" +
            $"<div style=\"display:flex;align-items:center;gap:12px;flex-wrap:wrap\">{Helpers.BadgeHtml(job.Status)}" +
            $"<span style=\"font-size:12px;color:var(--text-3);border-left:1px solid var(--border);padding-left:12px\">attempt {job.Attempts}/{job.MaxAttempts}</span></div>" +
            $"</div>" +
            (actions.Length > 0
                ? $"<div style=\"display:flex;gap:8px;align-items:flex-start;flex-wrap:wrap;flex-shrink:0\">{actions}</div>"
                : string.Empty) +
            $"</div>" +
            $"<div style=\"border-bottom:1px solid var(--border);margin-bottom:28px\"></div>";

        // Progress bar
        var progressSection = HtmlFragments.ProgressBar(job.ProgressPercent, job.ProgressMessage);

        // Visual execution flow timeline
        var executionTimeline = HtmlFragments.ExecutionTimeline(job, now);

        // Detail sections
        var timingRows = new List<(string Label, string Value)>();

        // Add timing rows if available
        if (job.ProcessingStartedAt.HasValue)
        {
            var enqueueLag = job.ProcessingStartedAt.Value - job.CreatedAt;
            timingRows.Add(("Enqueue → Start", Helpers.FormatCountdown(enqueueLag)));
        }

        if (job.ProcessingStartedAt.HasValue && job.CompletedAt.HasValue)
        {
            var duration = job.CompletedAt.Value - job.ProcessingStartedAt.Value;
            timingRows.Add(("Duration", Helpers.FormatCountdown(duration)));
        }

        timingRows.Add(("Total Age", Helpers.FormatCountdown(now - job.CreatedAt)));

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

            timingRows.Add(("Expires At", $"<span style=\"{expirationColor}\">{job.ExpiresAt.Value:yyyy-MM-dd HH:mm:ss UTC}</span>"));
        }

        timingRows.Add(("Retry At", job.RetryAt.HasValue ? $"{job.RetryAt.Value:yyyy-MM-dd HH:mm:ss UTC}" : "—"));

        var timingSection = HtmlFragments.DetailSection("Timing", timingRows.ToArray());

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
            "<div style=\"margin-bottom:28px;background:var(--surface);border:1px solid var(--border);border-radius:var(--radius-lg);padding:16px;box-shadow:0 1px 3px rgba(0,0,0,.2), inset 0 1px 0 rgba(255,255,255,.02)\">" +
            "<div class=\"section-title\" style=\"margin-bottom:12px\">Payload</div>" +
            $"<pre style=\"margin:0;border:none;background:var(--surface2);padding:12px;border-radius:6px;font-size:12px\">{Helpers.FormatJson(job.InputJson)}</pre>" +
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
            (IsReadOnly ? HtmlFragments.ReadOnlyBanner() : string.Empty) +
            header +
            progressSection +
            "<div class=\"timeline-section\">" +
            executionTimeline +
            "</div>" +
            "<div class=\"detail-sections\">" +
            timingSection +
            configuration +
            relationships +
            "</div>" +
            payloadSection +
            errorSection +
            logsSection +
            sseScript;

        return HtmlShell.Wrap(Title, PathPrefix, "jobs", body, Counters);
    }
}
