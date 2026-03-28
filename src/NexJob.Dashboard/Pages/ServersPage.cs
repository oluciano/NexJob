using System.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

internal sealed class ServersPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IStorageProvider Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/dashboard";
    [Parameter] public string Title { get; set; } = "NexJob";

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        // Default to a 1-minute timeout to consider a server active
        var activeServers = await Storage.GetActiveServersAsync(TimeSpan.FromMinutes(1));
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(activeServers)));
    }

    private string BuildHtml(IReadOnlyList<ServerRecord> servers)
    {
        if (servers.Count == 0)
        {
            var emptyBody =
                "<div class=\"page-header\"><div>" +
                "<h1 class=\"page-title\">Servers</h1>" +
                "<p class=\"page-subtitle\">Active worker nodes across the cluster</p>" +
                "</div></div>" +
                "<div class=\"empty-state\">" +
                "<svg width=\"40\" height=\"40\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1\"><rect x=\"2\" y=\"2\" width=\"20\" height=\"8\" rx=\"2\" ry=\"2\"/><rect x=\"2\" y=\"14\" width=\"20\" height=\"8\" rx=\"2\" ry=\"2\"/><line x1=\"6\" y1=\"6\" x2=\"6.01\" y2=\"6\"/><line x1=\"6\" y1=\"18\" x2=\"6.01\" y2=\"18\"/></svg>" +
                "<p>No active servers running.</p>" +
                "</div>";
            return HtmlShell.Wrap(Title, PathPrefix, "servers", emptyBody);
        }

        var tableBody = string.Join(string.Empty, servers.Select(s =>
        {
            var uptime = DateTimeOffset.UtcNow - s.StartedAt;
            var heartbeatAge = DateTimeOffset.UtcNow - s.HeartbeatAt;
            var isStale = heartbeatAge.TotalSeconds > 20;

            var badge = isStale
                ? "<span class=\"badge badge-warning\"><span class=\"dot dot-warning\"></span>stale</span>"
                : "<span class=\"badge badge-processing\"><span class=\"dot dot-processing\"></span>active</span>";

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

            return
                $"<tr>" +
                $"<td class=\"id-cell\" style=\"font-family:monospace;font-size:12px\" title=\"{HttpUtility.HtmlEncode(s.Id)}\">{HttpUtility.HtmlEncode(s.Id)}</td>" +
                $"<td>{badge}</td>" +
                $"<td><div title=\"Workers / Threads\">{s.WorkerCount}</div></td>" +
                $"<td class=\"truncate-cell\" title=\"{queuesStr}\">{queuesStr}</td>" +
                $"<td>{uptimeStr}</td>" +
                $"<td>{heartbeatStr}</td>" +
                $"</tr>";
        }));

        var totalWorkers = servers.Sum(s => s.WorkerCount);

        var body =
            "<div class=\"page-header\"><div>" +
            "<h1 class=\"page-title\">Servers</h1>" +
            $"<p class=\"page-subtitle\">{servers.Count} active node{(servers.Count == 1 ? string.Empty : "s")} processing {totalWorkers} concurrent jobs</p>" +
            "</div></div>" +
            "<div class=\"table-container\"><table class=\"data-table\">" +
            "<thead><tr>" +
            "<th style=\"width:30%\">Server ID</th>" +
            "<th style=\"width:10%\">State</th>" +
            "<th style=\"width:10%\">Capacity</th>" +
            "<th style=\"width:20%\">Queues</th>" +
            "<th style=\"width:15%\">Uptime</th>" +
            "<th style=\"width:15%\">Heartbeat</th>" +
            "</tr></thead>" +
            $"<tbody>{tableBody}</tbody>" +
            "</table></div>";

        return HtmlShell.Wrap(Title, PathPrefix, "servers", body);
    }
}
