using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

internal sealed class JobDetailPage : IComponent
{
    [Parameter] public IStorageProvider Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/jobs";
    [Parameter] public string Title { get; set; } = "NexJob";
    [Parameter] public JobId JobId { get; set; }

    private RenderHandle _handle;

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
            return HtmlShell.Wrap(Title, PathPrefix, "jobs",
                "<h2>Job not found</h2><p><a href=\"" + PathPrefix + "/jobs\">← Back to Jobs</a></p>");

        var actions = "";
        if (job.Status == JobStatus.Failed)
            actions +=
                $"<form method=\"post\" action=\"{PathPrefix}/jobs/{job.Id.Value}/requeue\" style=\"display:inline\">" +
                "<button type=\"submit\" class=\"btn btn-primary btn-sm\">Requeue</button></form> " +
                $"<form method=\"post\" action=\"{PathPrefix}/jobs/{job.Id.Value}/delete\" style=\"display:inline\">" +
                "<button type=\"submit\" class=\"btn btn-danger btn-sm\">Delete</button></form>";

        var rows = new[]
        {
            ("ID",          job.Id.Value.ToString()),
            ("Status",      Helpers.BadgeHtml(job.Status)),
            ("Type",        System.Web.HttpUtility.HtmlEncode(job.JobType)),
            ("Queue",       System.Web.HttpUtility.HtmlEncode(job.Queue)),
            ("Priority",    job.Priority.ToString()),
            ("Attempts",    $"{job.Attempts} / {job.MaxAttempts}"),
            ("Created",     job.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss UTC")),
            ("Scheduled",   job.ScheduledAt?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "—"),
            ("Started",     job.ProcessingStartedAt?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "—"),
            ("Completed",   job.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "—"),
            ("Retry At",    job.RetryAt?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "—"),
            ("Idempotency", job.IdempotencyKey is null ? "—" : System.Web.HttpUtility.HtmlEncode(job.IdempotencyKey)),
            ("Parent Job",  job.ParentJobId.HasValue ? $"<a href=\"{PathPrefix}/jobs/{job.ParentJobId.Value.Value}\">{job.ParentJobId.Value.Value}</a>" : "—"),
        };

        var detailGrid = string.Join("",
            rows.Select(r => $"<div class=\"detail-label\">{r.Item1}</div><div class=\"detail-value\">{r.Item2}</div>"));

        var payloadSection =
            "<h2>Payload</h2>" +
            $"<pre>{Helpers.FormatJson(job.InputJson)}</pre>";

        var errorSection = job.LastErrorMessage is null ? "" :
            "<h2 style=\"color:var(--danger);margin-top:20px\">Last Error</h2>" +
            $"<pre style=\"border-color:var(--danger)\">{System.Web.HttpUtility.HtmlEncode(job.LastErrorMessage)}</pre>" +
            (job.LastErrorStackTrace is null ? "" :
                "<h2 style=\"color:var(--danger);margin-top:12px\">Stack Trace</h2>" +
                $"<pre style=\"border-color:var(--danger)\">{System.Web.HttpUtility.HtmlEncode(job.LastErrorStackTrace)}</pre>");

        var body =
            $"<div style=\"display:flex;align-items:center;gap:16px;margin-bottom:24px\">" +
            $"<h1 class=\"page-title\" style=\"margin-bottom:0\">Job Detail</h1>" +
            $"{actions}" +
            $"</div>" +
            $"<a href=\"{PathPrefix}/jobs\" style=\"color:var(--text-muted);font-size:12px\">← Back to Jobs</a>" +
            "<div style=\"margin-top:20px\">" +
            $"<div class=\"detail-grid\">{detailGrid}</div>" +
            payloadSection +
            errorSection +
            "</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "jobs", body);
    }
}
