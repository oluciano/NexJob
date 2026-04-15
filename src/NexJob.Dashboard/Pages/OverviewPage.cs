using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

/// <summary>Dashboard overview page — metrics cards + throughput chart + recent failures.</summary>
internal sealed class OverviewPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IDashboardStorage Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/dashboard";
    [Parameter] public string Title { get; set; } = "NexJob";
    [Parameter] public NavCounters? Counters { get; set; }
    [Parameter] public JobMetrics? Metrics { get; set; }

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml()));
        return Task.CompletedTask;
    }

    private static (int Avg, HashSet<int> Anomalies) DetectAnomalies(
        IReadOnlyList<HourlyThroughput> hours)
    {
        if (hours.Count < 4)
        {
            return (0, new HashSet<int>());
        }

        var active = hours.Where(h => h.Count > 0).ToList();
        if (active.Count < 3)
        {
            return (0, new HashSet<int>());
        }

        var avg = (int)active.Average(h => h.Count);
        if (avg == 0)
        {
            return (0, new HashSet<int>());
        }

        var anomalies = hours
            .Select((h, i) => (h, i))
            .Where(x => x.h.Count > 0 && x.h.Count < avg * 0.4)
            .Select(x => x.i)
            .ToHashSet();

        return (avg, anomalies);
    }

    private string BuildHtml()
    {
        var m = Metrics ?? new JobMetrics();
        var now = DateTimeOffset.UtcNow;
        var max = m.HourlyThroughput.Count > 0 ? m.HourlyThroughput.Max(h => h.Count) : 1;

        var (avg, anomalyIndexes) = DetectAnomalies(m.HourlyThroughput);
        var avgPct = max > 0 ? (int)(avg * 140.0 / max) : 0;

        // Only label every 4th bar to avoid clutter
        var bars = string.Join(string.Empty, m.HourlyThroughput.Select((h, i) =>
        {
            var pct = max > 0 ? (int)(h.Count * 140.0 / max) : 2;
            var isAnomaly = anomalyIndexes.Contains(i);
            var cssClass = isAnomaly ? "bar anomaly" : "bar";
            var label = i % 4 == 0
                ? $"<span class=\"bar-label\">{h.Hour:HH}</span>"
                : string.Empty;
            var tooltip = $"{h.Hour:HH:mm} — {h.Count} job{(h.Count == 1 ? string.Empty : "s")}";
            return $"<div class=\"bar-wrap\"><div class=\"{cssClass}\" style=\"height:{pct}px\" data-tip=\"{System.Web.HttpUtility.HtmlAttributeEncode(tooltip)}\"></div>{label}</div>";
        }));

        var anomalyHours = anomalyIndexes
            .OrderBy(i => i)
            .Select(i => m.HourlyThroughput[i].Hour.ToString("HH") + "h")
            .ToList();
        var anomalyNote = anomalyHours.Count > 0
            ? $"<div class=\"anomaly-note\">⚠ Drop at {string.Join(", ", anomalyHours)} — below 40% of average</div>"
            : string.Empty;

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
            $"<div class=\"card card-enqueued\"><div class=\"card-header\"><span class=\"card-label\"><svg width=\"12\" height=\"12\" viewBox=\"0 0 16 16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><circle cx=\"8\" cy=\"8\" r=\"7\"/><path d=\"M8 4v4l3 3\"/></svg>ENQUEUED</span><span class=\"card-badge badge badge-blue\">waiting</span></div><div id=\"metric-enqueued\" class=\"card-value\">{m.Enqueued}</div><div class=\"card-delta\">Jobs waiting to run</div></div>" +
            $"<div class=\"card card-processing\"><div class=\"card-header\"><span class=\"card-label\"><svg width=\"12\" height=\"12\" viewBox=\"0 0 16 16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><path d=\"M8 1v14M1 8h14M4.5 4.5l7 7M11.5 4.5l-7 7\"/></svg>PROCESSING</span><span class=\"card-badge badge badge-orange\">active</span></div><div id=\"metric-processing\" class=\"card-value\">{m.Processing}</div><div class=\"card-delta\">Jobs currently running</div></div>" +
            $"<div class=\"card card-succeeded\"><div class=\"card-header\"><span class=\"card-label\"><svg width=\"12\" height=\"12\" viewBox=\"0 0 16 16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><path d=\"M2 8l4 4 8-8\"/></svg>SUCCEEDED</span><span class=\"card-badge badge badge-green\">done</span></div><div id=\"metric-succeeded\" class=\"card-value\">{m.Succeeded}</div><div class=\"card-delta\">Jobs completed today</div></div>" +
            $"<div class=\"card card-failed\"><div class=\"card-header\"><span class=\"card-label\"><svg width=\"12\" height=\"12\" viewBox=\"0 0 16 16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><path d=\"M4 4l8 8M12 4l-8 8\"/></svg>FAILED</span><span class=\"card-badge badge badge-red\">error</span></div><div id=\"metric-failed\" class=\"card-value\">{m.Failed}</div><div class=\"card-delta\">Jobs failed today</div></div>" +
            $"<div class=\"card card-scheduled\"><div class=\"card-header\"><span class=\"card-label\"><svg width=\"12\" height=\"12\" viewBox=\"0 0 16 16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><rect x=\"2\" y=\"3\" width=\"12\" height=\"11\" rx=\"2\"/><path d=\"M2 7h12M5 1v3M11 1v3\"/></svg>SCHEDULED</span><span class=\"card-badge badge badge-gray\">later</span></div><div id=\"metric-scheduled\" class=\"card-value\">{m.Scheduled}</div><div class=\"card-delta\">Future jobs waiting</div></div>" +
            $"<div class=\"card card-recurring\"><div class=\"card-header\"><span class=\"card-label\"><svg width=\"12\" height=\"12\" viewBox=\"0 0 16 16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><path d=\"M14 8A6 6 0 1 1 2 8a6 6 0 0 1 12 0z\"/><path d=\"M8 2v6l4 2\"/></svg>RECURRING</span><span class=\"card-badge badge badge-gray\">configured</span></div><div id=\"metric-recurring\" class=\"card-value\">{m.Recurring}</div><div class=\"card-delta\">Configured schedules</div></div>" +
            "</div>" +

            "<div class=\"chart\">" +
            "<div class=\"chart-header\"><span class=\"section-title\">Throughput — last 24h</span></div>" +
            (bars.Length > 0
                ? $"<div class=\"bars\" id=\"chart-bars\" data-avg-pct=\"{avgPct}\">{bars}</div><div class=\"chart-tooltip\" id=\"chart-tip\"></div>{anomalyNote}"
                : "<div class=\"empty-state\" style=\"padding:24px 0\"><p>No completed jobs in the last 24 hours.</p></div>") +
            "</div>" +

            "<div class=\"section\" id=\"overview-recent-failures\" data-refresh=\"true\">" +
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
            // average line
            $"(function(){{" +
            $"var el=document.getElementById('chart-bars');" +
            $"if(!el)return;" +
            $"var pct=parseInt(el.getAttribute('data-avg-pct')||'0');" +
            $"if(pct<4)return;" +
            $"var line=document.createElement('div');" +
            $"line.className='avg-line';" +
            $"line.style.bottom=pct+'px';" +
            $"el.style.position='relative';" +
            $"el.appendChild(line);" +
            $"}})();" +
            $"}})();</script>";

        return HtmlShell.Wrap(Title, PathPrefix, "overview", body, Counters, m);
    }
}
