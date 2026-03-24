using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

internal sealed class FailedPage : IComponent
{
    [Parameter] public IStorageProvider Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/jobs";
    [Parameter] public string Title { get; set; } = "NexJob";

    private RenderHandle _handle;

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        var filter = new JobFilter { Status = JobStatus.Failed };
        var result = await Storage.GetJobsAsync(filter, 1, 50);
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(result)));
    }

    private string BuildHtml(PagedResult<JobRecord> result)
    {
        var rows = string.Join("", result.Items.Select(j =>
            $"<tr>" +
            $"<td><a href=\"{PathPrefix}/jobs/{j.Id.Value}\">{j.Id.Value.ToString()[..8]}…</a></td>" +
            $"<td>{Helpers.ShortType(j.JobType)}</td>" +
            $"<td>{System.Web.HttpUtility.HtmlEncode(j.Queue)}</td>" +
            $"<td>{j.Attempts}</td>" +
            $"<td>{j.CompletedAt?.ToString("MM/dd HH:mm") ?? "—"}</td>" +
            $"<td>{Helpers.Truncate(j.LastErrorMessage, 80)}</td>" +
            $"<td>" +
            $"<form method=\"post\" action=\"{PathPrefix}/jobs/{j.Id.Value}/requeue\" style=\"display:inline\">" +
            $"<button type=\"submit\" class=\"btn btn-primary btn-sm\">Requeue</button></form> " +
            $"<form method=\"post\" action=\"{PathPrefix}/jobs/{j.Id.Value}/delete\" style=\"display:inline\">" +
            $"<button type=\"submit\" class=\"btn btn-danger btn-sm\">Delete</button></form>" +
            $"</td>" +
            $"</tr>"));

        var body =
            "<h1 class=\"page-title\">Failed Jobs</h1>" +
            "<div class=\"section\">" +
            (result.Items.Count == 0
                ? "<p style=\"color:var(--text-muted)\">No failed jobs. 🎉</p>"
                : $"<p style=\"color:var(--text-muted);margin-bottom:14px\">{result.TotalCount} failed job(s)</p>" +
                  "<table><thead><tr>" +
                  "<th>ID</th><th>Type</th><th>Queue</th><th>Attempts</th><th>Failed At</th><th>Error</th><th>Actions</th>" +
                  $"</tr></thead><tbody>{rows}</tbody></table>") +
            "</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "failed", body);
    }
}
