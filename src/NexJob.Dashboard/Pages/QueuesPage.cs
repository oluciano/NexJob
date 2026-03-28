using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

internal sealed class QueuesPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IStorageProvider Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/dashboard";
    [Parameter] public string Title { get; set; } = "NexJob";

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        var queues = await Storage.GetQueueMetricsAsync();
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(queues)));
    }

    private string BuildHtml(IReadOnlyList<QueueMetrics> queues)
    {
        if (queues.Count == 0)
        {
            var emptyBody =
                "<div class=\"page-header\"><div><h1 class=\"page-title\">Queues</h1><p class=\"page-subtitle\">Active processing queues</p></div></div>" +
                "<div class=\"empty-state\"><svg width=\"40\" height=\"40\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1\"><rect x=\"1\" y=\"10\" width=\"22\" height=\"4\" rx=\"1\"/><rect x=\"1\" y=\"6\" width=\"22\" height=\"3\" rx=\"1\" opacity=\".5\"/><rect x=\"1\" y=\"2\" width=\"22\" height=\"3\" rx=\"1\" opacity=\".25\"/></svg><p>No active queues.</p></div>";
            return HtmlShell.Wrap(Title, PathPrefix, "queues", emptyBody);
        }

        var cards = string.Join(string.Empty, queues.Select(q =>
        {
            var total = q.Enqueued + q.Processing;
            var utilPct = total > 0 ? (int)(q.Processing * 100.0 / total) : 0;

            return
                $"<div class=\"queue-card\">" +
                $"<div class=\"queue-card-header\">" +
                $"<div class=\"queue-name\">{System.Web.HttpUtility.HtmlEncode(q.Queue)}</div>" +
                $"<span class=\"badge {(q.Processing > 0 ? "badge-processing" : "badge-succeeded")}\">" +
                $"<span class=\"dot {(q.Processing > 0 ? "dot-processing" : "dot-succeeded")}\"></span>" +
                $"{(q.Processing > 0 ? "active" : "idle")}</span>" +
                $"</div>" +
                $"<div class=\"queue-metrics\">" +
                $"<div><div class=\"queue-metric-label\">Enqueued</div>" +
                $"<div class=\"queue-metric-val\" style=\"color:var(--info)\">{q.Enqueued}</div></div>" +
                $"<div><div class=\"queue-metric-label\">Processing</div>" +
                $"<div class=\"queue-metric-val\" style=\"color:var(--warning)\">{q.Processing}</div></div>" +
                $"<div><div class=\"queue-metric-label\">Total</div>" +
                $"<div class=\"queue-metric-val\">{total}</div></div>" +
                $"</div>" +
                $"<div class=\"queue-util-bar\"><div class=\"queue-util-fill\" style=\"width:{utilPct}%\"></div></div>" +
                $"<div class=\"queue-util-label\">{utilPct}% in-flight · " +
                $"<a href=\"{PathPrefix}/jobs?queue={Uri.EscapeDataString(q.Queue)}\" style=\"font-size:11px\">View jobs →</a></div>" +
                $"</div>";
        }));

        var body =
            "<div id=\"queues-page-content\" data-refresh=\"true\">" +
            "<div class=\"page-header\"><div>" +
            "<h1 class=\"page-title\">Queues</h1>" +
            $"<p class=\"page-subtitle\">{queues.Count} queue{(queues.Count == 1 ? string.Empty : "s")} active</p>" +
            "</div></div>" +
            $"<div class=\"queue-grid\">{cards}</div>" +
            "</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "queues", body);
    }
}
