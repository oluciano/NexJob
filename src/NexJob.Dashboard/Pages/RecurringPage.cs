using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

internal sealed class RecurringPage : IComponent
{
    [Parameter] public IStorageProvider Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/jobs";
    [Parameter] public string Title { get; set; } = "NexJob";

    private RenderHandle _handle;

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        var jobs = await Storage.GetRecurringJobsAsync();
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(jobs)));
    }

    private string BuildHtml(IReadOnlyList<RecurringJobRecord> jobs)
    {
        var now = DateTimeOffset.UtcNow;

        var rows = string.Join("", jobs.Select(r =>
        {
            var countdown = r.NextExecution.HasValue
                ? Helpers.FormatCountdown(r.NextExecution.Value - now)
                : "<span style=\"color:var(--text-muted)\">—</span>";

            var deleteForm =
                $"<form method=\"post\" action=\"{PathPrefix}/recurring/{Uri.EscapeDataString(r.RecurringJobId)}/delete\" style=\"display:inline\">" +
                "<button type=\"submit\" class=\"btn btn-danger btn-sm\">Delete</button></form>";

            return $"<tr>" +
                   $"<td>{System.Web.HttpUtility.HtmlEncode(r.RecurringJobId)}</td>" +
                   $"<td>{Helpers.ShortType(r.JobType)}</td>" +
                   $"<td><code>{System.Web.HttpUtility.HtmlEncode(r.Cron)}</code></td>" +
                   $"<td>{System.Web.HttpUtility.HtmlEncode(r.Queue)}</td>" +
                   $"<td>{r.LastExecutedAt?.ToString("MM/dd HH:mm") ?? "—"}</td>" +
                   $"<td>{countdown}</td>" +
                   $"<td>{deleteForm}</td>" +
                   $"</tr>";
        }));

        var body =
            "<h1 class=\"page-title\">Recurring Jobs</h1>" +
            "<div class=\"section\">" +
            (jobs.Count == 0
                ? "<p style=\"color:var(--text-muted)\">No recurring jobs registered.</p>"
                : "<table><thead><tr>" +
                  "<th>ID</th><th>Type</th><th>Cron</th><th>Queue</th><th>Last Run</th><th>Next Run</th><th>Actions</th>" +
                  $"</tr></thead><tbody>{rows}</tbody></table>") +
            "</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "recurring", body);
    }
}
