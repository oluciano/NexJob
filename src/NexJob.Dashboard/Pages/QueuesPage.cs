using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

internal sealed class QueuesPage : IComponent
{
    [Parameter] public IStorageProvider Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/jobs";
    [Parameter] public string Title { get; set; } = "NexJob";

    private RenderHandle _handle;

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        var queues = await Storage.GetQueueMetricsAsync();
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(queues)));
    }

    private string BuildHtml(IReadOnlyList<QueueMetrics> queues)
    {
        var rows = string.Join("", queues.Select(q =>
            $"<tr>" +
            $"<td><a href=\"{PathPrefix}/jobs?queue={Uri.EscapeDataString(q.Queue)}\">{System.Web.HttpUtility.HtmlEncode(q.Queue)}</a></td>" +
            $"<td><span style=\"color:var(--info)\">{q.Enqueued}</span></td>" +
            $"<td><span style=\"color:var(--warning)\">{q.Processing}</span></td>" +
            $"<td>{q.Enqueued + q.Processing}</td>" +
            $"</tr>"));

        var body =
            "<h1 class=\"page-title\">Queues</h1>" +
            "<div class=\"section\">" +
            (queues.Count == 0
                ? "<p style=\"color:var(--text-muted)\">No active queues.</p>"
                : "<table><thead><tr><th>Queue</th><th>Enqueued</th><th>Processing</th><th>Total</th></tr></thead>" +
                  $"<tbody>{rows}</tbody></table>") +
            "</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "queues", body);
    }
}
