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
    [Parameter] public JobStatus? StatusFilter { get; set; }
    [Parameter] public string? Search { get; set; }
    [Parameter] public string? TagFilter { get; set; }
    [Parameter] public int Page { get; set; } = 1;

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        var filter = new JobFilter { Status = StatusFilter, Search = Search };
        var result = await Storage.GetJobsAsync(filter, Page, 25);

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

    private static string BuildQs(string status, string? search, string? tag) =>
        $"?status={Uri.EscapeDataString(status)}" +
        (search?.Length > 0 ? $"&search={Uri.EscapeDataString(search)}" : string.Empty) +
        (tag?.Length > 0 ? $"&tag={Uri.EscapeDataString(tag)}" : string.Empty);

    private string BuildHtml(PagedResult<JobRecord> result)
    {
        var now = DateTimeOffset.UtcNow;
        var currentStatus = StatusFilter?.ToString() ?? string.Empty;
        var searchVal = System.Web.HttpUtility.HtmlAttributeEncode(Search ?? string.Empty);
        var tagVal = System.Web.HttpUtility.HtmlAttributeEncode(TagFilter ?? string.Empty);

        // Status pills
        var pills = string.Join(string.Empty, new[]
        {
            (string.Empty, "All"),
            ("Enqueued", "Enqueued"),
            ("Processing", "Processing"),
            ("Succeeded", "Succeeded"),
            ("Failed", "Failed"),
            ("Scheduled", "Scheduled"),
        }.Select(o =>
        {
            var active = currentStatus == o.Item1 ? " active" : string.Empty;
            var qs = BuildQs(o.Item1, Search, TagFilter);
            return $"<a href=\"{PathPrefix}/jobs{qs}\" class=\"status-pill{active}\">{o.Item2}</a>";
        }));

        var filters =
            $"<div class=\"filters\">" +
            $"<form method=\"get\" action=\"{PathPrefix}/jobs\" style=\"display:contents\">" +
            $"<input type=\"text\" name=\"search\" placeholder=\"Search type or ID…\" value=\"{searchVal}\" />" +
            $"<input type=\"text\" name=\"tag\" placeholder=\"Tag…\" value=\"{tagVal}\" style=\"min-width:130px\" />" +
            $"<input type=\"hidden\" name=\"status\" value=\"{System.Web.HttpUtility.HtmlAttributeEncode(currentStatus)}\" />" +
            $"<button type=\"submit\" class=\"btn btn-ghost btn-sm\">Search</button>" +
            (searchVal.Length > 0 || tagVal.Length > 0
                ? $"<a href=\"{PathPrefix}/jobs{BuildQs(currentStatus, null, null)}\" class=\"btn btn-ghost btn-sm\">Clear</a>"
                : string.Empty) +
            $"</form>" +
            $"<div class=\"status-pills\">{pills}</div>" +
            $"</div>";

        var rows = string.Join(string.Empty, result.Items.Select(j =>
        {
            var timeCell = j.Status switch
            {
                JobStatus.Scheduled =>
                    j.ScheduledAt.HasValue
                        ? $"<span style=\"color:var(--accent-light)\">{Helpers.CountdownFriendly(j.ScheduledAt.Value - now)}</span>"
                        : "—",
                JobStatus.Succeeded or JobStatus.Failed =>
                    Helpers.RelativeTime(j.CompletedAt, now),
                JobStatus.Processing =>
                    $"<span style=\"color:var(--warning)\">running</span>",
                _ => "—",
            };

            var tagBadges = j.Tags.Count > 0
                ? string.Join(" ", j.Tags.Select(t =>
                    $"<a href=\"{PathPrefix}/jobs?tag={Uri.EscapeDataString(t)}\" class=\"tag-badge\">{System.Web.HttpUtility.HtmlEncode(t)}</a>"))
                : string.Empty;

            var tagsRow = tagBadges.Length > 0
                ? $"<div class=\"job-row-tags\">{tagBadges}</div>"
                : string.Empty;

            var attemptInfo = j.Attempts > 0
                ? $"<span>attempt {j.Attempts}/{j.MaxAttempts}</span>"
                : string.Empty;

            return
                $"<a href=\"{PathPrefix}/jobs/{j.Id.Value}\" style=\"text-decoration:none\">" +
                $"<div class=\"job-row\">" +
                $"<div class=\"job-row-dot\">{Helpers.StatusDot(j.Status)}</div>" +
                $"<div class=\"job-row-main\">" +
                $"<div class=\"job-row-title\">{System.Web.HttpUtility.HtmlEncode(Helpers.ShortType(j.JobType))}</div>" +
                $"<div class=\"job-row-sub\">" +
                $"<span style=\"font-family:monospace;font-size:11px;color:var(--text-3)\">{j.Id.Value.ToString()[..8]}…</span>" +
                $"<span>{System.Web.HttpUtility.HtmlEncode(j.Queue)}</span>" +
                $"<span>priority {j.Priority}</span>" +
                (attemptInfo.Length > 0 ? $"{attemptInfo}" : string.Empty) +
                $"</div>" +
                tagsRow +
                $"</div>" +
                $"<div class=\"job-row-meta\">{timeCell}<br/><span style=\"font-size:11px\">{j.CreatedAt:MM/dd HH:mm}</span></div>" +
                $"</div></a>";
        }));

        var list = result.Items.Count == 0
            ? "<div class=\"empty-state\"><svg width=\"40\" height=\"40\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1\"><circle cx=\"12\" cy=\"12\" r=\"9\"/><line x1=\"12\" y1=\"8\" x2=\"12\" y2=\"12\"/><line x1=\"12\" y1=\"16\" x2=\"12.01\" y2=\"16\"/></svg><p>No jobs found matching your filters.</p></div>"
            : $"<div class=\"job-list\">{rows}</div>";

        var pagination = BuildPagination(result);

        var body =
            "<div class=\"page-header\"><div>" +
            "<h1 class=\"page-title\">Jobs</h1>" +
            $"<p class=\"page-subtitle\">{result.TotalCount} job{(result.TotalCount == 1 ? string.Empty : "s")} total</p>" +
            "</div></div>" +
            filters +
            "<div class=\"section\">" +
            list +
            pagination +
            "</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "jobs", body);
    }

    private string BuildPagination(PagedResult<JobRecord> result)
    {
        if (result.TotalPages <= 1)
        {
            return string.Empty;
        }

        var qs = $"?status={Uri.EscapeDataString(StatusFilter?.ToString() ?? string.Empty)}&search={Uri.EscapeDataString(Search ?? string.Empty)}&tag={Uri.EscapeDataString(TagFilter ?? string.Empty)}";

        var prev = result.Page > 1
            ? $"<a href=\"{PathPrefix}/jobs{qs}&page={result.Page - 1}\" class=\"btn btn-ghost btn-sm\">← Prev</a>"
            : "<span class=\"btn btn-ghost btn-sm\" style=\"opacity:.3;cursor:default\">← Prev</span>";

        var next = result.Page < result.TotalPages
            ? $"<a href=\"{PathPrefix}/jobs{qs}&page={result.Page + 1}\" class=\"btn btn-ghost btn-sm\">Next →</a>"
            : "<span class=\"btn btn-ghost btn-sm\" style=\"opacity:.3;cursor:default\">Next →</span>";

        return $"<div class=\"pagination\">{prev}{next}<span class=\"page-info\">Page {result.Page} of {result.TotalPages} ({result.TotalCount} jobs)</span></div>";
    }
}
