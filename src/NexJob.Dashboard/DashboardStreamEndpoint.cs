using System.Text.Json;
using Microsoft.AspNetCore.Http;
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
    /// Each event carries a compact JSON object with the six numeric counters.
    /// </summary>
    internal static async Task HandleAsync(HttpContext context, IStorageProvider storage)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers["Cache-Control"] = "no-cache";
        context.Response.Headers["X-Accel-Buffering"] = "no"; // disable nginx proxy buffering

        var ct = context.RequestAborted;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var metrics = await storage.GetMetricsAsync(ct);

                var payload = JsonSerializer.Serialize(new
                {
                    metrics.Enqueued,
                    metrics.Processing,
                    metrics.Succeeded,
                    metrics.Failed,
                    metrics.Scheduled,
                    metrics.Recurring,
                }, JsonOptions);

                await context.Response.WriteAsync($"data: {payload}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);

                await Task.Delay(PollInterval, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected or host shutting down — normal exit
        }
    }
}
