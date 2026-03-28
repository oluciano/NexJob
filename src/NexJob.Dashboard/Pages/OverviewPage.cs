using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

/// <summary>Dashboard overview page — metrics cards + throughput chart + recent failures.</summary>
internal sealed class OverviewPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IStorageProvider Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/jobs";
    [Parameter] public string Title { get; set; } = "NexJob";

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        var metrics = await Storage.GetMetricsAsync();
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(metrics)));
    }

    private string BuildHtml(JobMetrics m)
    {
        var now = DateTimeOffset.UtcNow;
        var max = m.HourlyThroughput.Count > 0 ? m.HourlyThroughput.Max(h => h.Count) : 1;

        // Only label every 4th bar to avoid clutter
        var bars = string.Join(string.Empty, m.HourlyThroughput.Select((h, i) =>
        {
            var pct = max > 0 ? (int)(h.Count * 140.0 / max) : 2;
            var label = i % 4 == 0
                ? $"<span class=\"bar-label\">{h.Hour:HH}</span>"
                : string.Empty;
            var tooltip = $"{h.Hour:HH:mm} — {h.Count} job{(h.Count == 1 ? string.Empty : "s")}";
            return $"<div class=\"bar-wrap\"><div class=\"bar\" style=\"height:{pct}px\" data-tip=\"{System.Web.HttpUtility.HtmlAttributeEncode(tooltip)}\"></div>{label}</div>";
        }));

        var failCards = m.RecentFailures.Count == 0
            ? "<div class=\"empty-state\" style=\"padding:32px 0\"><p>No failures recently — things look good.</p></div>"
            : string.Join(string.Empty, m.RecentFailures.Take(5).Select(j =>
                $"<a href=\"{PathPrefix}/jobs/{j.Id.Value}\" style=\"text-decoration:none\">" +
                $"<div class=\"job-row\" style=\"margin-bottom:0\">" +
                $"<div class=\"job-row-dot\">{Helpers.StatusDot(JobStatus.Failed)}</div>" +
                $"<div class=\"job-row-main\">" +
                $"<div class=\"job-row-title\">{System.Web.HttpUtility.HtmlEncode(Helpers.ShortType(j.JobType))}</div>" +
                $"<div class=\"job-row-sub\">" +
                $"<span>{System.Web.HttpUtility.HtmlEncode(j.Queue)}</span>" +
                $"<span style=\"color:var(--danger);font-size:11px\">{System.Web.HttpUtility.HtmlEncode(Helpers.Truncate(j.LastErrorMessage, 80))}</span>" +
                $"</div></div>" +
                $"<div class=\"job-row-meta\">{Helpers.RelativeTime(j.CompletedAt, now)}</div>" +
                $"</div></a>"));

        var body =
            "<div class=\"page-header\">" +
            "<div><h1 class=\"page-title\">Overview</h1><p class=\"page-subtitle\">Real-time job processing status</p></div>" +
            "</div>" +

            "<div class=\"cards\">" +
            $"<div class=\"card card-enqueued\"><div class=\"card-header\"><div class=\"card-label\"><span class=\"dot dot-enqueued\"></span>Enqueued</div></div><div id=\"metric-enqueued\" class=\"card-value\">{m.Enqueued}</div></div>" +
            $"<div class=\"card card-processing\"><div class=\"card-header\"><div class=\"card-label\"><span class=\"dot dot-processing\"></span>Processing</div></div><div id=\"metric-processing\" class=\"card-value\">{m.Processing}</div></div>" +
            $"<div class=\"card card-succeeded\"><div class=\"card-header\"><div class=\"card-label\"><span class=\"dot dot-succeeded\"></span>Succeeded</div></div><div id=\"metric-succeeded\" class=\"card-value\">{m.Succeeded}</div></div>" +
            $"<div class=\"card card-failed\"><div class=\"card-header\"><div class=\"card-label\"><span class=\"dot dot-failed\"></span>Failed</div></div><div id=\"metric-failed\" class=\"card-value\">{m.Failed}</div></div>" +
            $"<div class=\"card card-scheduled\"><div class=\"card-header\"><div class=\"card-label\"><span class=\"dot dot-scheduled\"></span>Scheduled</div></div><div id=\"metric-scheduled\" class=\"card-value\">{m.Scheduled}</div></div>" +
            $"<div class=\"card card-recurring\"><div class=\"card-header\"><div class=\"card-label\"><span class=\"dot dot-default\"></span>Recurring</div></div><div id=\"metric-recurring\" class=\"card-value\">{m.Recurring}</div></div>" +
            "</div>" +

            "<div class=\"chart\">" +
            "<div class=\"chart-header\"><span class=\"section-title\">Throughput — last 24h</span></div>" +
            (bars.Length > 0
                ? $"<div class=\"bars\" id=\"chart-bars\">{bars}</div><div class=\"chart-tooltip\" id=\"chart-tip\"></div>"
                : "<div class=\"empty-state\" style=\"padding:24px 0\"><p>No completed jobs in the last 24 hours.</p></div>") +
            "</div>" +

            "<div class=\"section\">" +
            "<div class=\"section-title\">Recent Failures</div>" +
            "<div class=\"job-list\">" + failCards + "</div>" +
            (m.RecentFailures.Count > 0
                ? $"<div style=\"margin-top:10px\"><a href=\"{PathPrefix}/failed\" style=\"font-size:12px;color:var(--text-3)\">View all failed →</a></div>"
                : string.Empty) +
            "</div>" +

            // SSE + chart tooltip JS
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
            // chart tooltip
            $"var tip=document.getElementById('chart-tip');" +
            $"if(tip){{document.querySelectorAll('.bar').forEach(function(b){{" +
            $"b.addEventListener('mouseenter',function(e){{tip.textContent=b.getAttribute('data-tip');tip.style.display='block';tip.style.left=(e.clientX+12)+'px';tip.style.top=(e.clientY-32)+'px';}});" +
            $"b.addEventListener('mousemove',function(e){{tip.style.left=(e.clientX+12)+'px';tip.style.top=(e.clientY-32)+'px';}});" +
            $"b.addEventListener('mouseleave',function(){{tip.style.display='none';}});" +
            $"}});}}" +
            $"}})();</script>";

        return HtmlShell.Wrap(Title, PathPrefix, "overview", body);
    }
}
