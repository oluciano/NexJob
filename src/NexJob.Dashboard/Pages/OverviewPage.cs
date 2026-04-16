using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

/// <summary>Dashboard overview page — metrics cards + throughput chart + recent failures.</summary>
[ExcludeFromCodeCoverage]
internal sealed class OverviewPage : IComponent
{
    private RenderHandle _handle;
    private JobMetrics? _fetchedMetrics;
    private PagedResult<JobRecord>? _recentJobs;
    private IReadOnlyList<QueueMetrics>? _queueMetrics;
    private IReadOnlyList<ServerRecord>? _activeServers;
    private IReadOnlyList<RecurringJobRecord>? _recurringJobsSummary;

    [Parameter] public IDashboardStorage Storage { get; set; } = default!;
    [Parameter] public IJobStorage JobStorage { get; set; } = default!;
    [Parameter] public IRecurringStorage RecurringStorage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/dashboard";
    [Parameter] public string Title { get; set; } = "NexJob";
    [Parameter] public NavCounters? Counters { get; set; }
    [Parameter] public JobMetrics? Metrics { get; set; }

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);

        var ct = CancellationToken.None;
        _fetchedMetrics = await Storage.GetMetricsAsync(ct).ConfigureAwait(false);
        _recentJobs = await Storage.GetJobsAsync(new JobFilter(), 1, 5, ct).ConfigureAwait(false);
        _queueMetrics = await Storage.GetQueueMetricsAsync(ct).ConfigureAwait(false);
        _activeServers = await JobStorage.GetActiveServersAsync(TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);

        var allRecurring = await RecurringStorage.GetRecurringJobsAsync(ct).ConfigureAwait(false);
        _recurringJobsSummary = allRecurring
            .Where(r => r.Enabled && !r.DeletedByUser && r.NextExecution.HasValue)
            .OrderBy(r => r.NextExecution)
            .Take(3)
            .ToList();

        _handle.Render(b => b.AddMarkupContent(0, BuildHtml()));
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
        var m = _fetchedMetrics ?? Metrics ?? new JobMetrics();
        var now = DateTimeOffset.UtcNow;

        // Ensure we always have 24 hours for the chart (Requirement 2 & 3)
        var throughput = m.HourlyThroughput.ToList();
        if (throughput.Count == 0)
        {
            for (var i = 0; i < 24; i++)
            {
                throughput.Add(new HourlyThroughput { Hour = now.AddHours(-23 + i), Count = 0 });
            }
        }

        var max = throughput.Count > 0 ? throughput.Max(h => h.Count) : 1;

        var (avg, anomalyIndexes) = DetectAnomalies(throughput);
        var avgPct = max > 0 ? (int)(avg * 140.0 / max) : 0;

        // Only label every 4th bar to avoid clutter
        var bars = string.Join(string.Empty, throughput.Select((h, i) =>
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
            .Select(i => throughput[i].Hour.ToString("HH") + "h")
            .ToList();
        var anomalyNote = anomalyHours.Count > 0
            ? $"<div class=\"anomaly-note\">⚠ Drop at {string.Join(", ", anomalyHours)} — below 40% of average</div>"
            : string.Empty;

        // 1. TOP METRICS
        var topMetricsHtml =
            "<div class=\"stats-grid\">" +
            HtmlFragments.MetricCard("metric-succeeded", "Succeeded", m.Succeeded, "stat-icon-success", "<svg width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><polyline points=\"20 6 9 17 4 12\"/></svg>", "Completed today", $"{PathPrefix}/jobs?status=Succeeded") +
            HtmlFragments.MetricCard("metric-processing", "Processing", m.Processing, "stat-icon-warning", "<svg width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><circle cx=\"12\" cy=\"12\" r=\"10\"/><polyline points=\"12 6 12 12 16 14\"/></svg>", "Currently active", $"{PathPrefix}/jobs?status=Processing") +
            HtmlFragments.MetricCard("metric-failed", "Failed", m.Failed, "stat-icon-error", "<svg width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><circle cx=\"12\" cy=\"12\" r=\"10\"/><line x1=\"15\" y1=\"9\" x2=\"9\" y2=\"15\"/><line x1=\"9\" y1=\"9\" x2=\"15\" y2=\"15\"/></svg>", "Errors today", $"{PathPrefix}/failed") +
            HtmlFragments.MetricCard("metric-recurring", "Recurring", m.Recurring, "stat-icon-info", "<svg width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><polyline points=\"23 4 23 10 17 10\"/><polyline points=\"1 20 1 14 7 14\"/><path d=\"M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15\"/></svg>", "Active schedules", $"{PathPrefix}/recurring") +
            "</div>";

        // 2. RECENT JOBS
        var recentJobsSb = new StringBuilder();
        recentJobsSb.Append("<div class=\"card\"><div class=\"card-header\"><h3>Recent Jobs</h3><a href=\"").Append(PathPrefix).Append("/jobs\" class=\"btn btn-secondary btn-sm\">View All</a></div>");
        if (_recentJobs?.Items.Count > 0)
        {
            recentJobsSb.Append("<table class=\"table\"><thead><tr><th>ID</th><th>Type</th><th>Status</th><th>Started</th><th>Duration</th><th style=\"text-align:right\">Action</th></tr></thead><tbody>");
            foreach (var j in _recentJobs.Items)
            {
                var duration = "—";
                if (j.ProcessingStartedAt.HasValue)
                {
                    var end = j.CompletedAt ?? now;
                    var d = end - j.ProcessingStartedAt.Value;
                    duration = d.TotalSeconds < 1 ? "< 1s" : $"{(int)d.TotalSeconds}s";
                }

                recentJobsSb.Append("<tr>")
                    .Append("<td style=\"font-family:monospace;font-size:12px\">#").Append(j.Id.Value.ToString()[..8]).Append("</td>")
                    .Append("<td>").Append(System.Web.HttpUtility.HtmlEncode(Helpers.ShortType(j.JobType))).Append("</td>")
                    .Append("<td>").Append(HtmlFragments.StatusBadge(j.Status.ToString())).Append("</td>")
                    .Append("<td>").Append(Helpers.RelativeTime(j.ProcessingStartedAt ?? j.CreatedAt, now)).Append("</td>")
                    .Append("<td>").Append(duration).Append("</td>")
                    .Append("<td style=\"text-align:right\"><a href=\"").Append(PathPrefix).Append("/jobs/").Append(j.Id.Value).Append("\" class=\"btn btn-secondary btn-sm\">View</a></td>")
                    .Append("</tr>");
            }

            recentJobsSb.Append("</tbody></table>");
        }
        else
        {
            recentJobsSb.Append("<div style=\"padding:32px;text-align:center;color:var(--text-tertiary)\">No jobs found.</div>");
        }

        recentJobsSb.Append("</div>");

        // 3. ACTIVE SERVERS
        var serversSb = new StringBuilder();
        serversSb.Append("<div class=\"card\"><div class=\"card-header\"><h3>Active Servers</h3><a href=\"").Append(PathPrefix).Append("/servers\" class=\"btn btn-secondary btn-sm\">Details</a></div><div style=\"padding:0\">");
        if (_activeServers?.Count > 0)
        {
            foreach (var s in _activeServers)
            {
                serversSb.Append("<div style=\"padding:12px 20px;display:flex;align-items:center;justify-content:space-between;border-bottom:1px solid var(--border)\">")
                    .Append("<div style=\"display:flex;align-items:center;gap:12px\">")
                    .Append("<span class=\"dot dot-succeeded\"></span>")
                    .Append("<div><div style=\"font-weight:600\">").Append(System.Web.HttpUtility.HtmlEncode(s.Id)).Append("</div><div style=\"font-size:11px;color:var(--text-tertiary)\">").Append(System.Web.HttpUtility.HtmlEncode(string.Join(", ", s.Queues))).Append("</div></div>")
                    .Append("</div>")
                    .Append("<div style=\"display:flex;gap:24px;align-items:center\">")
                    .Append("<div style=\"text-align:center\"><div style=\"font-size:10px;color:var(--text-tertiary);font-weight:700\">WORKERS</div><div style=\"font-weight:700\">").Append(s.WorkerCount).Append("</div></div>")
                    .Append("<div style=\"text-align:center\"><div style=\"font-size:10px;color:var(--text-tertiary);font-weight:700\">CPU</div><div style=\"font-weight:700\">—</div></div>")
                    .Append("</div></div>");
            }
        }
        else
        {
            serversSb.Append("<div style=\"padding:32px;text-align:center;color:var(--text-tertiary)\">No active servers.</div>");
        }

        serversSb.Append("</div></div>");

        // 4. RECURRING JOBS SUMMARY
        var recurringSummarySb = new StringBuilder();
        recurringSummarySb.Append("<div class=\"card\"><div class=\"card-header\"><h3>Upcoming Recurring</h3></div><div style=\"padding:0\">");
        if (_recurringJobsSummary?.Count > 0)
        {
            foreach (var r in _recurringJobsSummary)
            {
                var next = r.NextExecution.HasValue ? Helpers.CountdownFriendly(r.NextExecution.Value - now) : "—";
                recurringSummarySb.Append("<div style=\"padding:12px 20px;border-bottom:1px solid var(--border)\">")
                    .Append("<div style=\"display:flex;justify-content:space-between;align-items:center\">")
                    .Append("<div style=\"font-weight:600;color:var(--primary)\">").Append(System.Web.HttpUtility.HtmlEncode(r.RecurringJobId)).Append("</div>")
                    .Append("<div style=\"font-size:12px;color:var(--text-secondary)\">").Append(next).Append("</div>")
                    .Append("</div>")
                    .Append("<div style=\"font-size:11px;color:var(--text-tertiary);margin-top:2px\">").Append(System.Web.HttpUtility.HtmlEncode(Helpers.ShortType(r.JobType))).Append("</div>")
                    .Append("</div>");
            }
        }
        else
        {
            recurringSummarySb.Append("<div style=\"padding:32px;text-align:center;color:var(--text-tertiary)\">No recurring jobs scheduled.</div>");
        }

        recurringSummarySb.Append("</div></div>");

        // 5. QUEUE STATISTICS
        var queueStatsSb = new StringBuilder();
        queueStatsSb.Append("<div class=\"card\"><div class=\"card-header\"><h3>Queue Distribution</h3></div><div style=\"padding:20px\">");
        if (_queueMetrics?.Count > 0)
        {
            var totalJobs = _queueMetrics.Sum(q => q.Enqueued + q.Processing);
            foreach (var q in _queueMetrics.OrderByDescending(q => q.Enqueued + q.Processing))
            {
                var count = q.Enqueued + q.Processing;
                var pct = totalJobs > 0 ? (int)(count * 100.0 / totalJobs) : 0;
                queueStatsSb.Append("<div style=\"margin-bottom:16px\">")
                    .Append("<div style=\"display:flex;justify-content:space-between;margin-bottom:4px;font-size:13px\">")
                    .Append("<span style=\"font-weight:600\">").Append(System.Web.HttpUtility.HtmlEncode(q.Queue)).Append("</span>")
                    .Append("<span style=\"color:var(--text-secondary)\">").Append(count).Append(" jobs (").Append(pct).Append("%)</span>")
                    .Append("</div>")
                    .Append("<div style=\"height:8px;background:var(--bg-tertiary);border-radius:4px;overflow:hidden\">")
                    .Append("<div style=\"width:").Append(pct).Append("%;height:100%;background:var(--primary)\"></div>")
                    .Append("</div></div>");
            }
        }
        else
        {
            queueStatsSb.Append("<div style=\"text-align:center;color:var(--text-tertiary)\">No active queues.</div>");
        }

        queueStatsSb.Append("</div></div>");

        var body =
            HtmlFragments.PageHeader("Overview", "Real-time job processing status") +
            topMetricsHtml +
            "<div style=\"display:grid;grid-template-columns: 2fr 1fr; gap:24px\">" +
            "<div>" +
            "<div class=\"chart\" style=\"margin-bottom:24px\">" +
            "<div class=\"chart-header\"><span class=\"section-title\">Throughput — last 24h</span></div>" +
            $"<div class=\"bars\" id=\"chart-bars\" data-avg-pct=\"{avgPct}\">{bars}</div><div class=\"chart-tooltip\" id=\"chart-tip\"></div>{anomalyNote}" +
            "</div>" +
            recentJobsSb.ToString() +
            "</div>" +
            "<div>" +
            serversSb.ToString() +
            recurringSummarySb.ToString() +
            queueStatsSb.ToString() +
            "</div>" +
            "</div>" +

            // SSE + chart tooltip JS
            $"<script>(function(){{" +
            $"var es=new EventSource('{PathPrefix}/stream');" +
            $"es.onmessage=function(e){{" +
            $"var m=JSON.parse(e.data);" +
            $"if(document.getElementById('metric-enqueued'))document.getElementById('metric-enqueued').textContent=m.enqueued;" +
            $"if(document.getElementById('metric-processing'))document.getElementById('metric-processing').textContent=m.processing;" +
            $"if(document.getElementById('metric-succeeded'))document.getElementById('metric-succeeded').textContent=m.succeeded;" +
            $"if(document.getElementById('metric-failed'))document.getElementById('metric-failed').textContent=m.failed;" +
            $"if(document.getElementById('metric-recurring'))document.getElementById('metric-recurring').textContent=m.recurring;" +
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
