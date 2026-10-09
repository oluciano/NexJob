using System.Diagnostics.CodeAnalysis;
using System.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

[ExcludeFromCodeCoverage]
internal sealed class ServersPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IJobStorage Storage { get; set; } = default!;
    [Parameter] public IDashboardStorage? DashboardStorage { get; set; }
    [Parameter] public string PathPrefix { get; set; } = "/dashboard";
    [Parameter] public string Title { get; set; } = "NexJob";
    [Parameter] public NavCounters? Counters { get; set; }
    [Parameter] public IReadOnlyList<DashboardCluster>? Clusters { get; set; }
    [Parameter] public DashboardCluster? ActiveCluster { get; set; }

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);

        // NOTE (Blazor Dispatcher Invariant):
        // Do NOT use .ConfigureAwait(false) here. Rendering via _handle.Render requires execution on the Dispatcher.
        // Default to a 1-minute timeout to consider a server active
        var activeServers = await Storage.GetActiveServersAsync(TimeSpan.FromMinutes(1));

        var unservedQueues = new List<QueueMetrics>();
        var polledQueues = activeServers.SelectMany(s => s.Queues).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var effectiveDashboardStorage = DashboardStorage ?? ActiveCluster?.DashboardStorage ?? (Storage as IDashboardStorage);
        if (effectiveDashboardStorage != null)
        {
            var servedQueues = new HashSet<string>(activeServers.SelectMany(s => s.Queues), StringComparer.OrdinalIgnoreCase);
            var queueMetrics = await effectiveDashboardStorage.GetQueueMetricsAsync();
            unservedQueues = queueMetrics.Where(q => q.Enqueued > 0 && !servedQueues.Contains(q.Queue)).ToList();
        }

        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(activeServers, unservedQueues, polledQueues)));
    }

    private string BuildHtml(IReadOnlyList<ServerRecord> servers, IReadOnlyList<QueueMetrics> unservedQueues, IReadOnlyCollection<string> polledQueues)
    {
        var warningBanner = string.Empty;
        if (unservedQueues.Count > 0)
        {
            var queueNames = string.Join(", ", unservedQueues.Select(q => $"<strong>{HttpUtility.HtmlEncode(q.Queue)}</strong> ({q.Enqueued} enqueued)"));
            var hints = string.Concat(unservedQueues.Take(5)
                .Select(q => (q.Queue, Hint: OrphanQueueHint.For(q.Queue, polledQueues)))
                .Where(x => x.Hint is not null)
                .Select(x => $"<div style=\"font-size:12px;margin-top:6px\"><strong>{HttpUtility.HtmlEncode(x.Queue)}</strong>: {HttpUtility.HtmlEncode(x.Hint)}</div>"));
            warningBanner =
                $"<div style=\"background:var(--warning-light);border:1px solid var(--warning);border-radius:8px;padding:14px 18px;margin-bottom:20px;display:flex;align-items:flex-start;gap:12px;color:var(--text-primary)\">" +
                $"<span style=\"font-size:20px;line-height:1\">⚠️</span>" +
                $"<div>" +
                $"<div style=\"font-weight:600;color:var(--warning);margin-bottom:2px\">Unattended Queues Detected</div>" +
                $"<div style=\"font-size:13px;line-height:1.5\">The following queues have pending jobs waiting, but no active worker nodes in this cluster are configured to process them: {queueNames}. Jobs will remain enqueued until a worker listening to these queues is started.</div>" +
                hints +
                $"</div>" +
                $"</div>";
        }

        if (servers.Count == 0)
        {
            var emptyBody =
                HtmlFragments.Breadcrumbs(PathPrefix, ("Servers", null)) +
                HtmlFragments.PageHeader("Servers", "Active worker nodes across the cluster") +
                warningBanner +
                HtmlFragments.EmptyState(HtmlFragments.EmptyIconServer, "No active servers running.");
            return HtmlShell.Wrap(Title, PathPrefix, "servers", emptyBody, Counters, clusters: Clusters, activeCluster: ActiveCluster);
        }

        var (cpuPercent, workingSetMb, memPercent, _) = HostSystemMetrics.GetCurrent();
        var cpuColor = HtmlFragments.GetCpuColor(cpuPercent);
        var ramColor = HtmlFragments.GetRamColor(memPercent);

        var tableBody = string.Join(string.Empty, servers.Select(s =>
        {
            var uptime = DateTimeOffset.UtcNow - s.StartedAt;
            var heartbeatAge = DateTimeOffset.UtcNow - s.HeartbeatAt;
            var isStale = heartbeatAge.TotalSeconds > 20;

            var badgeClass = isStale ? "badge-warning" : "badge-success";
            var badge = $"<span class=\"badge {badgeClass}\">{(isStale ? "stale" : "active")}</span>";

            string uptimeStr;
            if (uptime.TotalDays >= 1)
            {
                uptimeStr = $"{(int)uptime.TotalDays}d {uptime.Hours}h";
            }
            else if (uptime.TotalHours >= 1)
            {
                uptimeStr = $"{uptime.Hours}h {uptime.Minutes}m";
            }
            else
            {
                uptimeStr = $"{uptime.Minutes}m";
            }

            var heartbeatStr = $"<span title=\"Last heartbeat {heartbeatAge.TotalSeconds:F0}s ago\">" +
                $"{(heartbeatAge.TotalSeconds < 5 ? "just now" : $"{heartbeatAge.TotalSeconds:F0}s ago")}" +
                $"</span>";

            var queuesStr = string.Join(", ", s.Queues.Select(HttpUtility.HtmlEncode));
            if (string.IsNullOrEmpty(queuesStr))
            {
                queuesStr = "-";
            }

            var cpuGauge = HtmlFragments.CircularGauge(cpuPercent, $"{cpuPercent}%", "CPU", $"{cpuPercent}%", cpuColor, size: 34);
            var memGauge = HtmlFragments.CircularGauge(memPercent, $"{workingSetMb}M", "RAM", $"{workingSetMb} MB", ramColor, size: 34);

            return
                $"<tr>" +
                $"<td style=\"font-family:monospace;font-size:12px\" title=\"{HttpUtility.HtmlAttributeEncode(s.Id)}\">{Helpers.FormatServerIdHtml(s.Id)}</td>" +
                $"<td>{badge}</td>" +
                $"<td>{s.WorkerCount}</td>" +
                $"<td>{cpuGauge}</td>" +
                $"<td>{memGauge}</td>" +
                $"<td>{queuesStr}</td>" +
                $"<td>{uptimeStr}</td>" +
                $"<td>{heartbeatStr}</td>" +
                $"</tr>";
        }));

        var totalWorkers = servers.Sum(s => s.WorkerCount);

        var body =
            "<div id=\"servers-page-content\" data-refresh=\"true\">" +
            HtmlFragments.Breadcrumbs(PathPrefix, ("Servers", null)) +
            HtmlFragments.PageHeader("Servers", "Active worker nodes across the cluster") +
            warningBanner +
            "<div class=\"card\">" +
            $"<div class=\"card-header\"><h3>{servers.Count} active node{(servers.Count == 1 ? string.Empty : "s")} processing {totalWorkers} concurrent jobs</h3></div>" +
            "<div class=\"table-container\"><table class=\"table\">" +
            "<thead><tr>" +
            "<th style=\"width:24%\">Server ID</th>" +
            "<th style=\"width:8%\">State</th>" +
            "<th style=\"width:8%\">Capacity</th>" +
            "<th style=\"width:15%\">CPU</th>" +
            "<th style=\"width:15%\">Memory</th>" +
            "<th style=\"width:12%\">Queues</th>" +
            "<th style=\"width:9%\">Uptime</th>" +
            "<th style=\"width:9%\">Heartbeat</th>" +
            "</tr></thead>" +
            $"<tbody>{tableBody}</tbody>" +
            "</table></div>" +
            "</div>" +
            "</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "servers", body, Counters, clusters: Clusters, activeCluster: ActiveCluster);
    }
}
