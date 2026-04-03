using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

internal sealed class JobsPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IStorageProvider Storage { get; set; } = default!;
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
        var filter = new JobFilter { Status = StatusFilter, Search = Search };
        var result = await Storage.GetJobsAsync(filter, Page, 25);

        // Apply in-memory queue filter
        if (!string.IsNullOrWhiteSpace(QueueFilter))
        {
            result = new PagedResult<JobRecord>
            {
                Items = result.Items.Where(j => j.Queue == QueueFilter).ToList(),
                TotalCount = result.TotalCount,
                Page = result.Page,
                PageSize = result.PageSize,
            };
        }

        // Apply in-memory tag filter (IStorageProvider.GetJobsByTagAsync returns all matches,
        // but we need paged results, so filter client-side from the already-paged set here)
        if (!string.IsNullOrWhiteSpace(TagFilter))
        {
            var taggedIds = (await Storage.GetJobsByTagAsync(TagFilter.Trim()))
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

        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(result)));
    }

    private string BuildHtml(PagedResult<JobRecord> result)
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
            HtmlFragments.PageHeader("Jobs", $"{result.TotalCount} job{(result.TotalCount == 1 ? string.Empty : "s")} total") +
            HtmlFragments.FilterBar(PathPrefix, currentStatus, Search, TagFilter, QueueFilter) +
            $"<div class=\"section\">" +
            list +
            pagination +
            $"</div>" +
            $"</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "jobs", body, Counters);
    }
}
