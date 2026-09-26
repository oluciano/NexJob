using System.Diagnostics.CodeAnalysis;
using System.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob;

namespace NexJob.Dashboard.Pages;

/// <summary>
/// Dashboard page listing active event triggers and broker listeners.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class ListenersPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IReadOnlyList<ListenerSnapshot> Listeners { get; set; } = Array.Empty<ListenerSnapshot>();
    [Parameter] public string PathPrefix { get; set; } = "/dashboard";
    [Parameter] public string Title { get; set; } = "NexJob";
    [Parameter] public NavCounters? Counters { get; set; }
    [Parameter] public IReadOnlyList<DashboardCluster>? Clusters { get; set; }
    [Parameter] public DashboardCluster? ActiveCluster { get; set; }

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);

        // NOTE (Blazor Dispatcher Invariant):
        // Do NOT use .ConfigureAwait(false) here. Rendering via _handle.Render requires execution on the Dispatcher.
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(Listeners)));
        return Task.CompletedTask;
    }

    private string BuildHtml(IReadOnlyList<ListenerSnapshot> listeners)
    {
        if (listeners.Count == 0)
        {
            var emptyBody =
                HtmlFragments.Breadcrumbs(PathPrefix, ("Listeners", null)) +
                HtmlFragments.PageHeader("Event Listeners", "Active message broker triggers and queue listeners") +
                HtmlFragments.EmptyState("12 2a10 10 0 0 0-10 10c0 4.42 2.87 8.17 6.84 9.5.5.08.66-.23.66-.5v-1.69c-2.77.6-3.36-1.34-3.36-1.34-.46-1.16-1.11-1.47-1.11-1.47-.91-.62.07-.6.07-.6 1 .07 1.53 1.03 1.53 1.03.87 1.52 2.34 1.07 2.91.83.09-.65.35-1.09.63-1.34-2.22-.25-4.55-1.11-4.55-4.92 0-1.11.38-2 1.03-2.71-.1-.25-.45-1.29.1-2.64 0 0 .84-.27 2.75 1.02.79-.22 1.65-.33 2.5-.33.85 0 1.71.11 2.5.33 1.91-1.29 2.75-1.02 2.75-1.02.55 1.35.2 2.39.1 2.64.65.71 1.03 1.6 1.03 2.71 0 3.82-2.34 4.66-4.57 4.91.36.31.69.92.69 1.85V21c0 .27.16.59.67.5C19.14 20.16 22 16.42 22 12A10 10 0 0 0 12 2z", "No event triggers or listeners currently registered.");
            return HtmlShell.Wrap(Title, PathPrefix, "listeners", emptyBody, Counters, clusters: Clusters, activeCluster: ActiveCluster);
        }

        var rows = string.Join(string.Empty, listeners.Select(l =>
        {
            var badgeClass = l.Status switch
            {
                ListenerStatus.Listening => "badge-success",
                ListenerStatus.Starting => "badge-info",
                ListenerStatus.Reconnecting => "badge-warning",
                ListenerStatus.Faulted => "badge-error",
                ListenerStatus.Stopped => "badge-gray",
                _ => "badge-gray",
            };

            var statusBadge = $"<span class=\"badge {badgeClass}\">{l.Status}</span>";
            var now = DateTimeOffset.UtcNow;
            var uptime = now - l.StartedAt;

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
                uptimeStr = $"{uptime.Minutes}m {uptime.Seconds}s";
            }

            var jobTagQuery = !string.IsNullOrWhiteSpace(l.JobTag)
                ? $"<a href=\"{PathPrefix}/jobs?tag={HttpUtility.UrlEncode(l.JobTag)}\" class=\"btn btn-secondary btn-sm\">View Jobs</a>"
                : "—";

            var endpointDisplay = HttpUtility.HtmlEncode(l.Endpoint);
            if (!string.IsNullOrWhiteSpace(l.ConsumerGroup))
            {
                endpointDisplay += $" <span style=\"font-size:12px;color:var(--text-tertiary)\">({HttpUtility.HtmlEncode(l.ConsumerGroup)})</span>";
            }

            var descriptionDisplay = !string.IsNullOrWhiteSpace(l.StatusDescription)
                ? $"<div style=\"font-size:11px;color:var(--text-secondary);margin-top:2px\">{HttpUtility.HtmlEncode(l.StatusDescription)}</div>"
                : string.Empty;

            return $"""
                <tr>
                    <td><strong>{HttpUtility.HtmlEncode(l.Broker)}</strong></td>
                    <td>{endpointDisplay}</td>
                    <td><code>{HttpUtility.HtmlEncode(l.TargetJobType)}</code></td>
                    <td>{statusBadge}{descriptionDisplay}</td>
                    <td>{uptimeStr}</td>
                    <td style="text-align:right">{jobTagQuery}</td>
                </tr>
                """;
        }));

        var activeCount = listeners.Count(l => l.Status == ListenerStatus.Listening);
        var subHeader = $"{activeCount} of {listeners.Count} active listener{(listeners.Count == 1 ? string.Empty : "s")} connected";

        var body =
            HtmlFragments.Breadcrumbs(PathPrefix, ("Listeners", null)) +
            HtmlFragments.PageHeader("Event Listeners", subHeader) +
            $"""
            <div class="card" style="padding:0;overflow:hidden">
                <table class="table">
                    <thead>
                        <tr>
                            <th>Broker</th>
                            <th>Endpoint / Group</th>
                            <th>Target Job</th>
                            <th>Status</th>
                            <th>Uptime</th>
                            <th style="text-align:right">Action</th>
                        </tr>
                    </thead>
                    <tbody>
                        {rows}
                    </tbody>
                </table>
            </div>
            """;

        return HtmlShell.Wrap(Title, PathPrefix, "listeners", body, Counters, clusters: Clusters, activeCluster: ActiveCluster);
    }
}
