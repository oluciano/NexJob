using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

[ExcludeFromCodeCoverage]
internal sealed class JobsPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IDashboardStorage Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/dashboard";
    [Parameter] public string Title { get; set; } = "NexJob";
    [Parameter] public NavCounters? Counters { get; set; }
    [Parameter] public JobStatus? StatusFilter { get; set; }
    [Parameter] public string? Search { get; set; }
    [Parameter] public string? TagFilter { get; set; }
    [Parameter] public string? QueueFilter { get; set; }
    [Parameter] public int Page { get; set; } = 1;

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);

        // Fetch queues for the filter dropdown
        var queues = await Storage.GetQueueMetricsAsync(CancellationToken.None).ConfigureAwait(false);

        // Use native storage filter for Status, Search, and Queue — much more efficient
        var filter = new JobFilter
        {
            Status = StatusFilter,
            Search = Search,
            Queue = QueueFilter,
        };

        var result = await Storage.GetJobsAsync(filter, Page, 50, CancellationToken.None).ConfigureAwait(false);

        // Apply in-memory tag filter (IStorageProvider doesn't have native Tag support in JobFilter yet,
        // so we still filter the current page client-side)
        if (!string.IsNullOrWhiteSpace(TagFilter))
        {
            var taggedIds = (await Storage.GetJobsByTagAsync(TagFilter.Trim()).ConfigureAwait(false))
                .Select(j => j.Id)
                .ToHashSet();
            result = new PagedResult<JobRecord>
            {
                Items = result.Items.Where(j => taggedIds.Contains(j.Id)).ToList(),
                TotalCount = result.TotalCount,
                Page = result.Page,
                PageSize = result.PageSize,
            };
        }

        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(result, queues)));
    }

    private string BuildHtml(PagedResult<JobRecord> result, IReadOnlyList<QueueMetrics> queues)
    {
        var now = DateTimeOffset.UtcNow;
        var currentStatus = StatusFilter?.ToString() ?? string.Empty;

        var list = result.Items.Count == 0
            ? HtmlFragments.EmptyState("0 0 24 24", "No jobs found matching your filters.")
            : $"<div class=\"job-list\">{string.Join(string.Empty, result.Items.Select(j => HtmlFragments.JobRow(j, PathPrefix, now)))}</div>";

        var baseUrl = $"{PathPrefix}/jobs?status={Uri.EscapeDataString(currentStatus)}&search={Uri.EscapeDataString(Search ?? string.Empty)}&tag={Uri.EscapeDataString(TagFilter ?? string.Empty)}&queue={Uri.EscapeDataString(QueueFilter ?? string.Empty)}";
        var pagination = HtmlFragments.Pagination(result, baseUrl);

        var body =
            $"<div id=\"jobs-page-content\" data-refresh=\"true\">" +
            HtmlFragments.Breadcrumbs(PathPrefix, ("Jobs", null)) +
            HtmlFragments.PageHeader("Jobs", "Browse and search all background jobs") +
            HtmlFragments.FilterBar(PathPrefix, currentStatus, Search, TagFilter, QueueFilter, queues) +
            $"<div class=\"card\">" +
            $"<div class=\"card-header\"><h3>{result.TotalCount} job{(result.TotalCount == 1 ? string.Empty : "s")} found</h3></div>" +
            $"<div style=\"padding:24px\">" +
            list +
            pagination +
            $"</div>" +
            $"</div>" +
            $"</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "jobs", body, Counters);
    }
}
