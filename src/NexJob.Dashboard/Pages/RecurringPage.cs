using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

internal sealed class RecurringPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IRecurringStorage Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/dashboard";
    [Parameter] public string Title { get; set; } = "NexJob";
    [Parameter] public NavCounters? Counters { get; set; }

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        var jobs = await Storage.GetRecurringJobsAsync().ConfigureAwait(false);
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(jobs)));
    }

    private string BuildHtml(IReadOnlyList<RecurringJobRecord> jobs)
    {
        var now = DateTimeOffset.UtcNow;

        if (jobs.Count == 0)
        {
            var emptyBody =
                HtmlFragments.PageHeader("Recurring Jobs", "Scheduled cron jobs") +
                HtmlFragments.EmptyState("0 0 24 24", "No recurring jobs registered.");
            return HtmlShell.Wrap(Title, PathPrefix, "recurring", emptyBody, Counters);
        }

        var rows = string.Join(string.Empty, jobs.Select(j => HtmlFragments.RecurringRow(j, PathPrefix, now)));

        var body =
            "<div id=\"recurring-page-content\" data-refresh=\"true\">" +
            HtmlFragments.PageHeader("Recurring Jobs", "Automated background job schedules") +
            "<div class=\"card\">" +
            $"<div class=\"card-header\"><h3>{jobs.Count} job{(jobs.Count == 1 ? string.Empty : "s")} registered</h3></div>" +
            "<div class=\"table-container\">" +
            "<table class=\"table\">" +
            "<thead><tr>" +
            "<th style=\"width:32px\"></th>" +
            "<th>ID / Name</th>" +
            "<th>Cron</th>" +
            "<th>Queue</th>" +
            "<th>Last Run</th>" +
            "<th>Next Run</th>" +
            "<th style=\"text-align:right\">Actions</th>" +
            "</tr></thead>" +
            $"<tbody>{rows}</tbody>" +
            "</table>" +
            "</div>" +
            "</div>" +
            "</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "recurring", body, Counters);
    }
}
