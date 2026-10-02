using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

[ExcludeFromCodeCoverage]
internal sealed class RecurringPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IRecurringStorage Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/dashboard";
    [Parameter] public string Title { get; set; } = "NexJob";
    [Parameter] public NavCounters? Counters { get; set; }
    [Parameter] public IReadOnlyList<DashboardCluster>? Clusters { get; set; }
    [Parameter] public DashboardCluster? ActiveCluster { get; set; }

    /// <summary>Gets or sets the set of queue names that currently have at least one active worker listening.</summary>
    [Parameter] public IReadOnlySet<string>? ActiveWorkerQueues { get; set; }

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);

        // NOTE (Blazor Dispatcher Invariant):
        // Do NOT use .ConfigureAwait(false) here. Rendering via _handle.Render requires execution on the Dispatcher.
        var jobs = await Storage.GetRecurringJobsAsync();
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(jobs)));
    }

    private string BuildHtml(IReadOnlyList<RecurringJobRecord> jobs)
    {
        var now = DateTimeOffset.UtcNow;

        if (jobs.Count == 0)
        {
            var emptyBody =
                HtmlFragments.Breadcrumbs(PathPrefix, ("Recurring", null)) +
                HtmlFragments.PageHeader("Recurring Jobs", "Scheduled cron jobs") +
                HtmlFragments.EmptyState(HtmlFragments.EmptyIconInbox, "No recurring jobs registered.");
            return HtmlShell.Wrap(Title, PathPrefix, "recurring", emptyBody, Counters, clusters: Clusters, activeCluster: ActiveCluster);
        }

        var isReadOnly = ActiveCluster?.IsReadOnly == true;
        var rows = string.Join(string.Empty, jobs.Select(j => HtmlFragments.RecurringRow(j, PathPrefix, now, ActiveCluster, ActiveWorkerQueues)));
        var actionsHeader = !isReadOnly ? "<th style=\"text-align:right\">Actions</th>" : string.Empty;

        var activeCount = jobs.Count(j => j.Enabled && !j.DeletedByUser);
        var pausedCount = jobs.Count(j => !j.Enabled && !j.DeletedByUser);
        var deletedCount = jobs.Count(j => j.DeletedByUser);

        var statsSub =
            $"<span style=\"display:inline-flex;align-items:center;gap:6px\"><span class=\"dot dot-succeeded\" style=\"width:7px;height:7px\"></span> {activeCount} Active</span>" +
            $"<span style=\"color:var(--border)\">·</span>" +
            $"<span style=\"display:inline-flex;align-items:center;gap:6px\"><span class=\"dot dot-processing\" style=\"width:7px;height:7px\"></span> {pausedCount} Paused</span>" +
            (deletedCount > 0 ? $"<span style=\"color:var(--border)\">·</span><span style=\"display:inline-flex;align-items:center;gap:6px\"><span class=\"dot dot-failed\" style=\"width:7px;height:7px\"></span> {deletedCount} Deleted</span>" : string.Empty);

        var cardHeader =
            "<div class=\"card-header\" style=\"display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:12px\">" +
            "<div style=\"display:flex;align-items:center;gap:16px\">" +
            $"<h3 style=\"margin:0;font-size:15px;font-weight:600\">Registered Schedules ({jobs.Count})</h3>" +
            $"<div style=\"display:flex;align-items:center;gap:10px;font-size:12px;color:var(--text-secondary)\">{statsSub}</div>" +
            "</div>" +
            "</div>";

        var body =
            "<div id=\"recurring-page-content\" data-refresh=\"true\">" +
            (isReadOnly ? HtmlFragments.ReadOnlyBanner() : string.Empty) +
            HtmlFragments.Breadcrumbs(PathPrefix, ("Recurring", null)) +
            HtmlFragments.PageHeader("Recurring Jobs", "Automated background job schedules") +
            "<div class=\"card\">" +
            cardHeader +
            "<div class=\"table-container\">" +
            "<table class=\"table\">" +
            "<thead><tr>" +
            "<th style=\"width:100px\">Status</th>" +
            "<th>Job</th>" +
            "<th>Schedule</th>" +
            "<th>Queue</th>" +
            "<th>Last Run</th>" +
            "<th>Next Run</th>" +
            actionsHeader +
            "</tr></thead>" +
            $"<tbody>{rows}</tbody>" +
            "</table>" +
            "</div>" +
            "</div>" +
            "</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "recurring", body, Counters, clusters: Clusters, activeCluster: ActiveCluster);
    }
}
