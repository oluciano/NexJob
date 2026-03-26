using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

internal sealed class JobsPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IStorageProvider Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/jobs";
    [Parameter] public string Title { get; set; } = "NexJob";
    [Parameter] public JobStatus? StatusFilter { get; set; }
    [Parameter] public string? Search { get; set; }
    [Parameter] public int Page { get; set; } = 1;

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        var filter = new JobFilter { Status = StatusFilter, Search = Search };
        var result = await Storage.GetJobsAsync(filter, Page, 25);
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(result)));
    }

    private string BuildHtml(PagedResult<JobRecord> result)
    {
        var statusOptions = string.Join(string.Empty, new[]
        {
            (string.Empty, "All"),
            ("Enqueued", "Enqueued"),
            ("Processing", "Processing"),
            ("Succeeded", "Succeeded"),
            ("Failed", "Failed"),
            ("Scheduled", "Scheduled"),
            ("AwaitingContinuation", "Awaiting"),
        }.Select(o =>
        {
            var sel = (StatusFilter?.ToString() ?? string.Empty) == o.Item1 ? " selected" : string.Empty;
            return $"<option value=\"{o.Item1}\"{sel}>{o.Item2}</option>";
        }));

        var searchVal = System.Web.HttpUtility.HtmlAttributeEncode(Search ?? string.Empty);
        var filters =
            $"<form method=\"get\" action=\"{PathPrefix}/jobs\" class=\"filters\">" +
            $"<input type=\"text\" name=\"search\" placeholder=\"Search type or queue…\" value=\"{searchVal}\" />" +
            $"<select name=\"status\">{statusOptions}</select>" +
            $"<button type=\"submit\" class=\"btn btn-primary btn-sm\">Filter</button>" +
            $"</form>";

        var now = DateTimeOffset.UtcNow;
        var rows = string.Join(string.Empty, result.Items.Select(j =>
        {
            var timeCell = j.Status switch
            {
                JobStatus.Scheduled =>
                    j.ScheduledAt.HasValue
                        ? $"<span title=\"{j.ScheduledAt.Value:yyyy-MM-dd HH:mm:ss UTC}\" style=\"color:var(--accent-light)\">" +
                          $"{Helpers.FormatCountdown(j.ScheduledAt.Value - now)}</span>"
                        : "—",
                JobStatus.Succeeded or JobStatus.Failed =>
                    j.CompletedAt?.ToString("MM/dd HH:mm") ?? "—",
                JobStatus.Processing =>
                    $"<span style=\"color:var(--warning)\">running…</span>",
                _ => "—",
            };

            return $"<tr>" +
                   $"<td><a href=\"{PathPrefix}/jobs/{j.Id.Value}\">{j.Id.Value.ToString()[..8]}…</a></td>" +
                   $"<td>{Helpers.BadgeHtml(j.Status)}</td>" +
                   $"<td>{Helpers.ShortType(j.JobType)}</td>" +
                   $"<td>{System.Web.HttpUtility.HtmlEncode(j.Queue)}</td>" +
                   $"<td>{j.Priority}</td>" +
                   $"<td>{j.CreatedAt:MM/dd HH:mm}</td>" +
                   $"<td>{timeCell}</td>" +
                   $"</tr>";
        }));

        var table = result.Items.Count == 0
            ? "<p style=\"color:var(--text-muted)\">No jobs found.</p>"
            : $"<table><thead><tr><th>ID</th><th>Status</th><th>Type</th><th>Queue</th><th>Priority</th><th>Created</th><th>Runs At / Completed</th></tr></thead><tbody>{rows}</tbody></table>";

        var pagination = BuildPagination(result);

        var body =
            "<h1 class=\"page-title\">Jobs</h1>" +
            filters +
            "<div class=\"section\">" +
            table +
            pagination +
            "</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "jobs", body);
    }

    private string BuildPagination(PagedResult<JobRecord> result)
    {
        if (result.TotalPages <= 1) return string.Empty;

        var qs = $"?status={Uri.EscapeDataString(StatusFilter?.ToString() ?? string.Empty)}&search={Uri.EscapeDataString(Search ?? string.Empty)}";

        var prev = result.Page > 1
            ? $"<a href=\"{PathPrefix}/jobs{qs}&page={result.Page - 1}\" class=\"btn btn-primary btn-sm\">← Prev</a>"
            : "<span class=\"btn btn-sm\" style=\"opacity:.3\">← Prev</span>";

        var next = result.Page < result.TotalPages
            ? $"<a href=\"{PathPrefix}/jobs{qs}&page={result.Page + 1}\" class=\"btn btn-primary btn-sm\">Next →</a>"
            : "<span class=\"btn btn-sm\" style=\"opacity:.3\">Next →</span>";

        return $"<div class=\"pagination\">{prev}{next}<span class=\"page-info\">Page {result.Page} of {result.TotalPages} ({result.TotalCount} jobs)</span></div>";
    }
}
