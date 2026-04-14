using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NexJob.Storage;

namespace NexJob.Dashboard;

/// <summary>
/// Handles the <c>GET {prefix}/stream</c> endpoint that pushes metric snapshots to the browser
/// using Server-Sent Events (SSE). The connection stays open until the client disconnects or
/// the host shuts down.
/// </summary>
internal static class DashboardStreamEndpoint
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Streams <see cref="JobMetrics"/> snapshots as SSE events until the request is aborted.
    /// Each event carries a compact JSON object with counters, hourly throughput,
    /// and active job progress updates for live progress bars on the job detail page.
    /// </summary>
    internal static async Task HandleAsync(HttpContext context, IDashboardStorage storage)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers["Cache-Control"] = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no"; // disable nginx proxy buffering

        var cache = context.RequestServices.GetRequiredService<IMemoryCache>();
        var dashboardOptions = context.RequestServices.GetRequiredService<DashboardOptions>();

        var ct = context.RequestAborted;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var metrics = await GetCachedMetricsAsync(cache, storage, dashboardOptions, ct).ConfigureAwait(false);

                var activeResult = await storage.GetJobsAsync(
                    new JobFilter { Status = JobStatus.Processing }, 1, 20, ct).ConfigureAwait(false);

                var payload = JsonSerializer.Serialize(new
                {
                    metrics.Enqueued,
                    metrics.Processing,
                    metrics.Succeeded,
                    metrics.Failed,
                    metrics.Scheduled,
                    metrics.Recurring,
                    HourlyThroughput = metrics.HourlyThroughput.Select(h => new { h.Hour, h.Count }),
                    ActiveJobs = activeResult.Items.Select(j => new
                    {
                        Id = j.Id.Value,
                        j.ProgressPercent,
                        j.ProgressMessage,
                    }),
                }, JsonOptions);

                await context.Response.WriteAsync($"data: {payload}\n\n", ct).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(ct).ConfigureAwait(false);

                await Task.Delay(PollInterval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected or host shutting down — normal exit
        }
    }

    private static async Task<JobMetrics> GetCachedMetricsAsync(
        IMemoryCache cache, IDashboardStorage storage, DashboardOptions options, CancellationToken ct)
    {
        const string CacheKey = "nexjob:dashboard:metrics";

        // If cache TTL is zero, disable caching
        if (options.MetricsCacheTtl == TimeSpan.Zero)
        {
            return await storage.GetMetricsAsync(ct).ConfigureAwait(false);
        }

        if (cache.TryGetValue(CacheKey, out JobMetrics? cached) && cached is not null)
        {
            return cached;
        }

        var metrics = await storage.GetMetricsAsync(ct).ConfigureAwait(false);
        cache.Set(CacheKey, metrics, options.MetricsCacheTtl);

        return metrics;
    }
}
