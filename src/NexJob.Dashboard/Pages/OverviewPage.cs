using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

/// <summary>Dashboard overview page — metrics cards + throughput chart + recent failures.</summary>
internal sealed class OverviewPage : IComponent
{
    [Parameter] public IStorageProvider Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/jobs";
    [Parameter] public string Title { get; set; } = "NexJob";

    private RenderHandle _handle;

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        var metrics = await Storage.GetMetricsAsync();
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(metrics)));
    }

    private string BuildHtml(JobMetrics m)
    {
        var max  = m.HourlyThroughput.Count > 0 ? m.HourlyThroughput.Max(h => h.Count) : 1;
        var bars = string.Join("", m.HourlyThroughput.Select(h =>
        {
            var pct = max > 0 ? (int)(h.Count * 80.0 / max) : 2;
            return $"<div class=\"bar-wrap\"><div class=\"bar\" style=\"height:{pct}px\" title=\"{h.Hour:HH:mm} — {h.Count}\"></div><span class=\"bar-label\">{h.Hour:HH}</span></div>";
        }));

        var failRows = string.Join("", m.RecentFailures.Select(j =>
            $"<tr><td><a href=\"{PathPrefix}/jobs/{j.Id}\">{j.Id.Value.ToString()[..8]}…</a></td>" +
            $"<td>{Helpers.ShortType(j.JobType)}</td><td>{j.Queue}</td>" +
            $"<td>{j.CompletedAt?.ToString("MM/dd HH:mm") ?? "—"}</td>" +
            $"<td>{System.Web.HttpUtility.HtmlEncode(j.LastErrorMessage ?? "—")}</td></tr>"));

        var body =
            $"<h1 class=\"page-title\">Overview</h1>" +
            $"<div class=\"cards\">" +
            $"<div class=\"card\"><div class=\"card-label\">Enqueued</div><div id=\"metric-enqueued\" class=\"card-value enqueued\">{m.Enqueued}</div></div>" +
            $"<div class=\"card\"><div class=\"card-label\">Processing</div><div id=\"metric-processing\" class=\"card-value processing\">{m.Processing}</div></div>" +
            $"<div class=\"card\"><div class=\"card-label\">Succeeded</div><div id=\"metric-succeeded\" class=\"card-value succeeded\">{m.Succeeded}</div></div>" +
            $"<div class=\"card\"><div class=\"card-label\">Failed</div><div id=\"metric-failed\" class=\"card-value failed\">{m.Failed}</div></div>" +
            $"<div class=\"card\"><div class=\"card-label\">Scheduled</div><div id=\"metric-scheduled\" class=\"card-value scheduled\">{m.Scheduled}</div></div>" +
            $"<div class=\"card\"><div class=\"card-label\">Recurring</div><div id=\"metric-recurring\" class=\"card-value recurring\">{m.Recurring}</div></div>" +
            $"</div>" +
            $"<div class=\"chart\"><h2>Throughput — last 24h</h2>" +
            (bars.Length > 0 ? $"<div class=\"bars\">{bars}</div>" : "<p style=\"color:var(--text-muted)\">No completed jobs in the last 24 hours.</p>") +
            "</div>" +
            "<div class=\"section\"><h2>Recent Failures</h2>" +
            (m.RecentFailures.Count == 0
                ? "<p style=\"color:var(--text-muted)\">No failures. 🎉</p>"
                : $"<table><thead><tr><th>ID</th><th>Type</th><th>Queue</th><th>Failed At</th><th>Error</th></tr></thead><tbody>{failRows}</tbody></table>") +
            "</div>" +
            $"<script>(function(){{" +
            $"var es=new EventSource('{PathPrefix}/stream');" +
            $"es.onmessage=function(e){{" +
            $"var m=JSON.parse(e.data);" +
            $"document.getElementById('metric-enqueued').textContent=m.enqueued;" +
            $"document.getElementById('metric-processing').textContent=m.processing;" +
            $"document.getElementById('metric-succeeded').textContent=m.succeeded;" +
            $"document.getElementById('metric-failed').textContent=m.failed;" +
            $"document.getElementById('metric-scheduled').textContent=m.scheduled;" +
            $"document.getElementById('metric-recurring').textContent=m.recurring;" +
            $"}};" +
            $"es.onerror=function(){{es.close();}};" +
            $"}})();</script>";

        return HtmlShell.Wrap(Title, PathPrefix, "overview", body);
    }
}
