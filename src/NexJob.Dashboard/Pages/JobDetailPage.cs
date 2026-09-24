using System.Diagnostics.CodeAnalysis;
using System.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

[ExcludeFromCodeCoverage]
internal sealed class JobDetailPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IDashboardStorage Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/dashboard";
    [Parameter] public string Title { get; set; } = "NexJob";
    [Parameter] public NavCounters? Counters { get; set; }
    [Parameter] public JobId JobId { get; set; }
    [Parameter] public bool IsReadOnly { get; set; }

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);

        // NOTE (Blazor Dispatcher Invariant):
        // Do NOT use .ConfigureAwait(false) here. Rendering via _handle.Render requires execution on the Dispatcher.
        var job = await Storage.GetJobByIdAsync(JobId);
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(job)));
    }

    private string BuildHtml(JobRecord? job)
    {
        if (job is null)
        {
            var notFoundHtml =
                HtmlFragments.Breadcrumbs(PathPrefix, ("Jobs", $"{PathPrefix}/jobs"), ("Not Found", null)) +
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

        // Header with Breadcrumbs
        var header =
            HtmlFragments.Breadcrumbs(PathPrefix, ("Jobs", $"{PathPrefix}/jobs"), (vm.ShortType, null)) +
            $"<div style=\"display:flex;align-items:flex-start;justify-content:space-between;gap:20px;margin-bottom:32px;flex-wrap:wrap\">" +
            $"<div style=\"flex:1\">" +
            $"<h1 class=\"page-title\" style=\"margin-bottom:8px;font-size:32px\">{HttpUtility.HtmlEncode(vm.ShortType)}</h1>" +
            $"<div style=\"display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin-bottom:16px\">" +
            $"{Helpers.BadgeHtml(job.Status)}" +
            $"<span style=\"font-family:monospace;font-size:13px;color:var(--text-secondary);background:var(--bg-tertiary);padding:2px 8px;border-radius:4px\">{job.Id.Value}</span>" +
            $"<span style=\"font-size:12px;color:var(--text-tertiary);border-left:1px solid var(--border);padding-left:12px\">attempt {job.Attempts}/{job.MaxAttempts}</span>" +
            $"</div>" +
            $"</div>" +
            (actions.Length > 0
                ? $"<div style=\"display:flex;gap:8px;align-items:flex-start;flex-wrap:wrap;flex-shrink:0;padding-top:8px\">{actions}</div>"
                : string.Empty) +
            $"</div>";

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
            "<div style=\"margin-bottom:28px\">" +
            "<div style=\"display:flex;justify-content:space-between;align-items:center;margin-bottom:8px\">" +
            "<h3 style=\"font-size:14px;font-weight:600;margin:0\">Payload</h3>" +
            "</div>" +
            "<div class=\"terminal-window\">" +
            "<div class=\"terminal-header\"><div class=\"terminal-dots\"><span></span><span></span><span></span></div><span class=\"terminal-title\">payload.json</span><button class=\"copy-btn\" onclick=\"navigator.clipboard.writeText(this.parentElement.nextElementSibling.innerText);this.textContent='Copied!';setTimeout(()=>this.textContent='Copy',1500)\">Copy</button></div>" +
            $"<div class=\"terminal-body\"><pre style=\"margin:0;font-size:12px;color:#e2e8f0;overflow-x:auto;font-family:monospace;white-space:pre-wrap\">{Helpers.FormatJson(job.InputJson)}</pre></div>" +
            "</div>" +
            "</div>";

        // Error section
        var errorSection = HtmlFragments.ErrorSection(job.LastErrorMessage, job.LastErrorStackTrace);

        // Logs section
        var logsSection =
            $"<div style=\"margin-bottom:24px\">" +
            $"<div style=\"display:flex;justify-content:space-between;align-items:center;margin-bottom:8px\">" +
            $"<h3 style=\"font-size:14px;font-weight:600;margin:0\">Execution Logs <span id=\"logs-count-badge\" style=\"font-weight:400;color:var(--text-tertiary)\">({job.ExecutionLogs.Count} entries)</span></h3>" +
            (job.Status == JobStatus.Processing ? "<span class=\"badge badge-warning\"><span class=\"pulse-live\"></span> STREAMING LIVE</span>" : string.Empty) +
            $"</div>" +
            $"<div class=\"terminal-window\">" +
            $"<div class=\"terminal-header\"><div class=\"terminal-dots\"><span></span><span></span><span></span></div><span class=\"terminal-title\">job-{job.Id.Value.ToString()[..8]}.log</span><button class=\"copy-btn\" onclick=\"navigator.clipboard.writeText(this.parentElement.nextElementSibling.innerText);this.textContent='Copied!';setTimeout(()=>this.textContent='Copy',1500)\">Copy</button></div>" +
            $"<div id=\"terminal-logs-body\" class=\"terminal-body\" style=\"max-height:360px;overflow-y:auto;padding:12px 16px;font-family:monospace;font-size:12px\">" +
            (job.ExecutionLogs.Count > 0
                ? string.Join(string.Empty, job.ExecutionLogs.Select(entry =>
                {
                    var color = entry.Level switch
                    {
                        "Warning" => "var(--warning)",
                        "Error" or "Critical" => "var(--error)",
                        "Debug" or "Trace" => "var(--text-tertiary)",
                        _ => "var(--text-secondary)",
                    };
                    var ts = entry.Timestamp.ToString("HH:mm:ss.fff");
                    var msg = HttpUtility.HtmlEncode(entry.Message).Replace("\n", "&#10;");
                    return $"<div style=\"display:flex;gap:10px;line-height:1.6\"><span style=\"color:#94a3b8;flex-shrink:0\">[{ts}]</span><span style=\"color:{color};font-weight:600;min-width:70px;flex-shrink:0\">[{entry.Level}]</span><span style=\"color:#e2e8f0;word-break:break-all\">{msg}</span></div>";
                }))
                : "<p style=\"color:var(--text-tertiary);margin:0\">No logs captured for this execution.</p>") +
            $"</div>" +
            $"</div>" +
            $"</div>";

        // SSE for live progress and log streaming
        var sseScript =
            $"<script>(function(){{" +
            $"var jobId='{job.Id.Value}';" +
            $"var fill=document.getElementById('progress-bar-fill');" +
            $"var pctEl=document.getElementById('progress-pct');" +
            $"var msgEl=document.getElementById('progress-msg');" +
            $"var logsBody=document.getElementById('terminal-logs-body');" +
            $"var logsBadge=document.getElementById('logs-count-badge');" +
            $"var isRunning={(job.Status == JobStatus.Processing ? "true" : "false")};" +
            $"var lastLogsCount={job.ExecutionLogs.Count};" +
            $"var es=new EventSource('{PathPrefix}/stream');" +
            $"es.onmessage=function(e){{" +
            $"var d=JSON.parse(e.data);" +
            $"var jobs=d.activeJobs||[];" +
            $"var j=jobs.find(function(x){{return x.id===jobId;}});" +
            $"if(j&&j.progressPercent!==null&&j.progressPercent!==undefined&&fill){{" +
            $"fill.style.width=j.progressPercent+'%';" +
            $"if(pctEl)pctEl.textContent=j.progressPercent+'%';" +
            $"if(msgEl&&j.progressMessage)msgEl.textContent=j.progressMessage;" +
            $"}}" +
            $"if(isRunning){{" +
            $"fetch('{PathPrefix}/jobs/'+jobId+'/logs').then(r=>r.json()).then(logs=>{{" +
            $"if(Array.isArray(logs)&&logs.length>lastLogsCount&&logsBody){{" +
            $"lastLogsCount=logs.length;" +
            $"if(logsBadge)logsBadge.textContent='('+logs.length+' entries)';" +
            $"logsBody.innerHTML=logs.map(l=>{{" +
            $"var c=l.level==='Warning'?'var(--warning)':(l.level==='Error'||l.level==='Critical'?'var(--error)':'var(--text-secondary)');" +
            $"var t=l.timestamp?l.timestamp.split(' ')[1]||l.timestamp:'';" +
            $"return '<div style=\"display:flex;gap:10px;line-height:1.6\"><span style=\"color:#94a3b8;flex-shrink:0\">['+t+']</span><span style=\"color:'+c+';font-weight:600;min-width:70px;flex-shrink:0\">['+l.level+']</span><span style=\"color:#e2e8f0;word-break:break-all\">'+l.message+'</span></div>';" +
            $"}}).join('');" +
            $"logsBody.scrollTop=logsBody.scrollHeight;" +
            $"}}" +
            $"}}).catch(()=>{{}});" +
            $"}}" +
            $"}};es.onerror=function(){{es.close();}};" +
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
