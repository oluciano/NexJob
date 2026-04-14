using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Configuration;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

internal sealed class QueuesPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IDashboardStorage Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/dashboard";
    [Parameter] public string Title { get; set; } = "NexJob";
    [Parameter] public NavCounters? Counters { get; set; }
    [Parameter] public NexJobOptions Options { get; set; } = default!;

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        var queues = await Storage.GetQueueMetricsAsync();
        var processingJobs = await Storage.GetJobsAsync(
            filter: new JobFilter { Status = JobStatus.Processing },
            page: 1,
            pageSize: 50);
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(queues, processingJobs.Items)));
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 1)
        {
            return "0.1s";
        }

        if (elapsed.TotalSeconds < 60)
        {
            return $"{elapsed.TotalSeconds:F1}s";
        }

        if (elapsed.TotalMinutes < 60)
        {
            return $"{elapsed.TotalMinutes:F1}m";
        }

        return $"{elapsed.TotalHours:F1}h";
    }

    private string BuildHtml(IReadOnlyList<QueueMetrics> queues, IReadOnlyList<JobRecord> processingJobs)
    {
        if (queues.Count == 0)
        {
            var emptyBody =
                "<div class=\"page-header\"><div><h1 class=\"page-title\">Queues</h1><p class=\"page-subtitle\">Active processing queues</p></div></div>" +
                "<div class=\"empty-state\"><svg width=\"40\" height=\"40\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1\"><rect x=\"1\" y=\"10\" width=\"22\" height=\"4\" rx=\"1\"/><rect x=\"1\" y=\"6\" width=\"22\" height=\"3\" rx=\"1\" opacity=\".5\"/><rect x=\"1\" y=\"2\" width=\"22\" height=\"3\" rx=\"1\" opacity=\".25\"/></svg><p>No active queues.</p></div>";
            return HtmlShell.Wrap(Title, PathPrefix, "queues", emptyBody, Counters);
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

        var heatmap = BuildWorkerHeatmap(processingJobs);

        var body =
            "<div id=\"queues-page-content\" data-refresh=\"true\">" +
            "<div class=\"page-header\"><div>" +
            "<h1 class=\"page-title\">Queues</h1>" +
            $"<p class=\"page-subtitle\">{queues.Count} queue{(queues.Count == 1 ? string.Empty : "s")} active</p>" +
            "</div></div>" +
            heatmap +
            $"<div class=\"queue-grid\">{cards}</div>" +
            "</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "queues", body, Counters);
    }

    private string BuildWorkerHeatmap(IReadOnlyList<JobRecord> processingJobs)
    {
        var now = DateTimeOffset.UtcNow;
        var workerCount = Options.Workers;

        if (processingJobs.Count == 0)
        {
            var workerRows = string.Join(string.Empty, Enumerable.Range(1, workerCount).Select(i =>
                $"<div class=\"worker-row idle\">" +
                $"<span class=\"worker-id\">W{i}</span>" +
                $"<div class=\"worker-track\"></div>" +
                $"<span class=\"worker-elapsed\">idle</span>" +
                $"<span class=\"worker-warn\"></span>" +
                $"</div>"));

            return
                $"<div class=\"worker-section\" data-refresh=\"true\">" +
                $"<div class=\"section-title\">Workers</div>" +
                $"<div class=\"worker-list\">{workerRows}</div>" +
                $"</div>";
        }

        // Calculate average elapsed time for slow detection
        var avgElapsed = processingJobs.Count > 1
            ? processingJobs.Average(j => (now - j.CreatedAt).TotalSeconds)
            : 0;
        var slowThreshold = avgElapsed * 3;

        var jobsByIndex = processingJobs.Take(workerCount).ToList();

        var rows = string.Join(string.Empty, Enumerable.Range(0, workerCount).Select(i =>
        {
            var job = i < jobsByIndex.Count ? jobsByIndex[i] : null;

            if (job is null)
            {
                return
                    $"<div class=\"worker-row idle\">" +
                    $"<span class=\"worker-id\">W{i + 1}</span>" +
                    $"<div class=\"worker-track\"></div>" +
                    $"<span class=\"worker-elapsed\">idle</span>" +
                    $"<span class=\"worker-warn\"></span>" +
                    $"</div>";
            }

            var elapsed = now - job.CreatedAt;
            var isSlow = processingJobs.Count > 1 && elapsed.TotalSeconds > slowThreshold;
            var fillWidth = processingJobs.Count > 0
                ? (int)(((elapsed.TotalSeconds / processingJobs.Max(j => (now - j.CreatedAt).TotalSeconds)) * 95) + 5)
                : 0;
            fillWidth = Math.Min(fillWidth, 100);

            var jobName = $"{Helpers.ShortType(job.JobType)} #{job.Id.Value.ToString()[..4]}";
            var elapsedStr = FormatElapsed(elapsed);
            var cssClass = isSlow ? "slow" : "busy";
            var warn = isSlow ? "⚠" : string.Empty;

            return
                $"<div class=\"worker-row\">" +
                $"<span class=\"worker-id\">W{i + 1}</span>" +
                $"<div class=\"worker-track\">" +
                $"<div class=\"worker-fill {cssClass}\" style=\"width:{fillWidth}%\">" +
                $"<span class=\"worker-job-name\">{System.Web.HttpUtility.HtmlEncode(jobName)}</span>" +
                $"</div>" +
                $"</div>" +
                $"<span class=\"worker-elapsed{(isSlow ? " slow" : string.Empty)}\">{elapsedStr}</span>" +
                $"<span class=\"worker-warn\">{warn}</span>" +
                $"</div>";
        }));

        return
            $"<div class=\"worker-section\" data-refresh=\"true\">" +
            $"<div class=\"section-title\">Workers</div>" +
            $"<div class=\"worker-list\">{rows}</div>" +
            $"</div>";
    }
}
