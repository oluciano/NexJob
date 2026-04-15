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
                HtmlFragments.PageHeader("Queues", "Active processing queues") +
                HtmlFragments.EmptyState("0 0 24 24", "No active queues.");
            return HtmlShell.Wrap(Title, PathPrefix, "queues", emptyBody, Counters);
        }

        var cards = string.Join(string.Empty, queues.Select(q => HtmlFragments.QueueCard(q, PathPrefix)));

        var heatmap = BuildWorkerHeatmap(processingJobs);

        var body =
            "<div id=\"queues-page-content\" data-refresh=\"true\">" +
            HtmlFragments.PageHeader("Queues", $"{queues.Count} queue{(queues.Count == 1 ? string.Empty : "s")} active") +
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
