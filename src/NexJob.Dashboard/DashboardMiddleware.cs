using System.Globalization;
using Cronos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NexJob.Configuration;
using NexJob.Dashboard.Pages;
using NexJob.Storage;

namespace NexJob.Dashboard;

/// <summary>
/// ASP.NET Core middleware that serves the NexJob dashboard at a configurable path prefix.
/// Renders Blazor components server-side using <see cref="HtmlRenderer"/> — no client-side
/// Blazor or SignalR required.
/// </summary>
public sealed class DashboardMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _pathPrefix;
    private readonly DashboardOptions _options;

    /// <summary>Creates a new instance of <see cref="DashboardMiddleware"/>.</summary>
    public DashboardMiddleware(RequestDelegate next, string pathPrefix, DashboardOptions options)
    {
        _next = next;
        _pathPrefix = pathPrefix.TrimEnd('/');
        _options = options;
    }

    /// <summary>Processes an incoming request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (!path.StartsWith(_pathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var authHandler = context.RequestServices.GetService<IDashboardAuthorizationHandler>();
        if (authHandler is not null && !await authHandler.AuthorizeAsync(context).ConfigureAwait(false))
        {
            context.Response.StatusCode = 401;
            return;
        }

        DashboardCluster? activeCluster = null;
        if (_options.Clusters.Count > 0)
        {
            var clusterId = context.Request.Query["cluster"].ToString();
            activeCluster = _options.Clusters.FirstOrDefault(c => string.Equals(c.Id, clusterId, StringComparison.OrdinalIgnoreCase))
                ?? _options.Clusters[0];
        }

        var subPath = path[_pathPrefix.Length..].TrimStart('/');

        // SSE metrics stream
        if (string.Equals(subPath, "stream", StringComparison.Ordinal) && string.Equals(context.Request.Method, HttpMethods.Get, StringComparison.Ordinal))
        {
            var storage = activeCluster?.DashboardStorage ?? context.RequestServices.GetRequiredService<IDashboardStorage>();
            await DashboardStreamEndpoint.HandleAsync(context, storage, _options, activeCluster).ConfigureAwait(false);
            return;
        }

        // GET /jobs/{id}/logs — returns execution logs as JSON for modal
        var logsSegments = subPath.Split('/');
        if (string.Equals(context.Request.Method, HttpMethods.Get, StringComparison.Ordinal) &&
            logsSegments.Length == 3 &&
            string.Equals(logsSegments[0], "jobs", StringComparison.Ordinal) &&
            string.Equals(logsSegments[2], "logs", StringComparison.Ordinal))
        {
            var logsStorage = activeCluster?.DashboardStorage ?? context.RequestServices.GetRequiredService<IDashboardStorage>();
            if (!Guid.TryParse(logsSegments[1], out var logsGuid))
            {
                context.Response.StatusCode = 400;
                return;
            }

            var logsJob = await logsStorage.GetJobByIdAsync(new JobId(logsGuid), context.RequestAborted).ConfigureAwait(false);
            if (logsJob is null)
            {
                context.Response.StatusCode = 404;
                return;
            }

            context.Response.ContentType = "application/json";
            var logs = logsJob.ExecutionLogs ?? Array.Empty<JobExecutionLog>();
            await context.Response.WriteAsJsonAsync(logs.Select(l => new
            {
                timestamp = l.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                level = l.Level,
                message = l.Message,
            }), context.RequestAborted).ConfigureAwait(false);
            return;
        }

        // Handle API actions (POST)
        if (string.Equals(context.Request.Method, HttpMethods.Post, StringComparison.Ordinal) && await HandleActionsAsync(context, subPath, activeCluster).ConfigureAwait(false))
        {
            return;
        }

        // Render page
        var html = await RenderPageAsync(context, subPath, activeCluster).ConfigureAwait(false);
        if (string.Equals(html, HtmlShell.NotFound(_options.Title, _pathPrefix), StringComparison.Ordinal))
        {
            context.Response.StatusCode = 404;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html).ConfigureAwait(false);
    }

    private static bool TryGetJobId(string subPath, out JobId jobId)
    {
        var parts = subPath.Split('/');
        if (parts.Length >= 2 && Guid.TryParse(parts[1], out var guid))
        {
            jobId = new JobId(guid);
            return true;
        }

        jobId = default;
        return false;
    }

    private static string GetRecurringId(string subPath) =>
        Uri.UnescapeDataString(subPath.Split('/')[1]);

    private static async Task<string> RenderAsync<TComponent>(
        HtmlRenderer renderer, ParameterView parameters)
        where TComponent : IComponent
#pragma warning disable MA0004
    {
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            // NOTE (Blazor Dispatcher Invariant):
            // Do NOT use .ConfigureAwait(false) here or in TComponent.SetParametersAsync.
            // Blazor SSR (HtmlRenderer) requires rendering (_handle.Render) to execute on the
            // Dispatcher's SynchronizationContext. ConfigureAwait(false) causes continuations
            // to resume on a ThreadPool worker, causing:
            // "System.InvalidOperationException: The current thread is not associated with the Dispatcher."
            var output = await renderer.RenderComponentAsync<TComponent>(parameters);
            return output.ToHtmlString();
#pragma warning restore MA0004
        }).ConfigureAwait(false);
    }

#pragma warning disable SCS0027
    private static void LocalRedirect(HttpContext context, string location, DashboardCluster? activeCluster = null)
    {
        if (location.StartsWith("/", StringComparison.Ordinal) && !location.StartsWith("//", StringComparison.Ordinal))
        {
            if (activeCluster is not null)
            {
                var separator = location.Contains('?', StringComparison.Ordinal) ? "&" : "?";
                location = $"{location}{separator}cluster={Uri.EscapeDataString(activeCluster.Id)}";
            }

            context.Response.Redirect(location);
        }
    }
#pragma warning restore SCS0027

    private static async Task<JobMetrics> GetCachedMetricsAsync(
        IMemoryCache cache, IDashboardStorage storage, DashboardOptions options, DashboardCluster? activeCluster, CancellationToken ct)
    {
        var cacheKey = activeCluster is not null
            ? $"nexjob:dashboard:metrics:{activeCluster.Id}"
            : "nexjob:dashboard:metrics";

        // If cache TTL is zero, disable caching
        if (options.MetricsCacheTtl == TimeSpan.Zero)
        {
            return await storage.GetMetricsAsync(ct).ConfigureAwait(false);
        }

        if (cache.TryGetValue(cacheKey, out JobMetrics? cached) && cached is not null)
        {
            return cached;
        }

        var metrics = await storage.GetMetricsAsync(ct).ConfigureAwait(false);
        cache.Set(cacheKey, metrics, options.MetricsCacheTtl);

        return metrics;
    }

    private async Task<bool> HandleActionsAsync(HttpContext context, string subPath, DashboardCluster? activeCluster)
    {
        if (activeCluster?.IsReadOnly == true)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return true;
        }

        var recurringStorage = activeCluster?.RecurringStorage
            ?? (activeCluster?.DashboardStorage as IRecurringStorage)
            ?? context.RequestServices.GetRequiredService<IRecurringStorage>();

        var dashboardStorage = activeCluster?.DashboardStorage
            ?? context.RequestServices.GetRequiredService<IDashboardStorage>();

        var controlService = activeCluster?.ControlService
            ?? context.RequestServices.GetRequiredService<IJobControlService>();

        var runtimeStore = activeCluster?.RuntimeStore
            ?? context.RequestServices.GetService<IRuntimeSettingsStore>()
            ?? context.RequestServices.GetRequiredService<IRuntimeSettingsStore>();

        if (subPath.StartsWith("jobs/", StringComparison.Ordinal))
        {
            return await TryHandleJobActionAsync(context, subPath, controlService, activeCluster).ConfigureAwait(false)
                || await TryHandleBulkActionAsync(context, subPath, dashboardStorage, controlService, activeCluster).ConfigureAwait(false);
        }

        if (subPath.StartsWith("recurring/", StringComparison.Ordinal))
        {
            return await TryHandleRecurringActionAsync(context, subPath, recurringStorage, runtimeStore, activeCluster).ConfigureAwait(false);
        }

        if (subPath.StartsWith("settings/", StringComparison.Ordinal)
            || subPath.StartsWith("queues/", StringComparison.Ordinal))
        {
            return await TryHandleSettingsActionAsync(context, subPath, runtimeStore, controlService, activeCluster).ConfigureAwait(false);
        }

        return false;
    }

    private async Task<bool> TryHandleJobActionAsync(
        HttpContext context, string subPath, IJobControlService controlService, DashboardCluster? activeCluster)
    {
        if (subPath.Contains("/runnow", StringComparison.Ordinal) && TryGetJobId(subPath, out var runNowId))
        {
            await controlService.RequeueJobAsync(runNowId, context.RequestAborted).ConfigureAwait(false);
            LocalRedirect(context, $"{_pathPrefix}/jobs/{runNowId.Value}", activeCluster);
            return true;
        }

        if (subPath.Contains("/requeue", StringComparison.Ordinal) && TryGetJobId(subPath, out var requeueId))
        {
            await controlService.RequeueJobAsync(requeueId, context.RequestAborted).ConfigureAwait(false);
            LocalRedirect(context, $"{_pathPrefix}/failed", activeCluster);
            return true;
        }

        if (subPath.Contains("/delete", StringComparison.Ordinal) && TryGetJobId(subPath, out var deleteId))
        {
            await controlService.DeleteJobAsync(deleteId, context.RequestAborted).ConfigureAwait(false);
            LocalRedirect(context, $"{_pathPrefix}/failed", activeCluster);
            return true;
        }

        return false;
    }

    private async Task<bool> TryHandleRecurringActionAsync(
        HttpContext context, string subPath, IRecurringStorage recurringStorage, IRuntimeSettingsStore runtimeStore, DashboardCluster? activeCluster)
    {
        if (subPath.Contains("/delete", StringComparison.Ordinal))
        {
            var recurringId = GetRecurringId(subPath);
            await recurringStorage.DeleteRecurringJobAsync(recurringId, context.RequestAborted).ConfigureAwait(false);
            LocalRedirect(context, $"{_pathPrefix}/recurring", activeCluster);
            return true;
        }

        if (subPath.Contains("/trigger", StringComparison.Ordinal))
        {
            var recurringId = GetRecurringId(subPath);
            await recurringStorage.SetRecurringJobNextExecutionAsync(
                recurringId, DateTimeOffset.UtcNow.AddSeconds(-1), context.RequestAborted).ConfigureAwait(false);
            LocalRedirect(context, $"{_pathPrefix}/recurring/{Uri.UnescapeDataString(recurringId)}", activeCluster);
            return true;
        }

        if (subPath.Contains("/pause", StringComparison.Ordinal))
        {
            var recurringId = GetRecurringId(subPath);
            var allJobs = await recurringStorage.GetRecurringJobsAsync(context.RequestAborted).ConfigureAwait(false);
            var existing = allJobs.FirstOrDefault(r => string.Equals(r.RecurringJobId, recurringId, StringComparison.Ordinal));
            if (existing is not null)
            {
                await recurringStorage.UpdateRecurringJobConfigAsync(recurringId, existing.CronOverride, enabled: false, context.RequestAborted).ConfigureAwait(false);
            }

            LocalRedirect(context, $"{_pathPrefix}/recurring/{Uri.UnescapeDataString(recurringId)}", activeCluster);
            return true;
        }

        if (subPath.Contains("/resume", StringComparison.Ordinal))
        {
            var recurringId = GetRecurringId(subPath);
            var allJobs = await recurringStorage.GetRecurringJobsAsync(context.RequestAborted).ConfigureAwait(false);
            var existing = allJobs.FirstOrDefault(r => string.Equals(r.RecurringJobId, recurringId, StringComparison.Ordinal));
            if (existing is not null)
            {
                await recurringStorage.UpdateRecurringJobConfigAsync(recurringId, existing.CronOverride, enabled: true, context.RequestAborted).ConfigureAwait(false);
            }

            LocalRedirect(context, $"{_pathPrefix}/recurring/{Uri.UnescapeDataString(recurringId)}", activeCluster);
            return true;
        }

        if (subPath.Contains("/update-config", StringComparison.Ordinal))
        {
            var recurringId = GetRecurringId(subPath);
            var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            var cronOverrideRaw = form["cronOverride"].ToString();

            string? cronOverride = null;
            if (!string.IsNullOrWhiteSpace(cronOverrideRaw))
            {
                // Validate cron expression before persisting
                try
                {
                    try { CronExpression.Parse(cronOverrideRaw, CronFormat.IncludeSeconds); }
                    catch (CronFormatException) { CronExpression.Parse(cronOverrideRaw, CronFormat.Standard); }

                    cronOverride = cronOverrideRaw.Trim();
                }
                catch (CronFormatException)
                {
                    // Invalid cron — redirect back without saving
                    LocalRedirect(context, $"{_pathPrefix}/recurring/{Uri.EscapeDataString(recurringId)}", activeCluster);
                    return true;
                }
            }

            var allJobs = await recurringStorage.GetRecurringJobsAsync(context.RequestAborted).ConfigureAwait(false);
            var existing = allJobs.FirstOrDefault(r => string.Equals(r.RecurringJobId, recurringId, StringComparison.Ordinal));
            if (existing is not null)
            {
                await recurringStorage.UpdateRecurringJobConfigAsync(recurringId, cronOverride, existing.Enabled, context.RequestAborted).ConfigureAwait(false);

                // Recalculate next execution immediately so changes take effect
                var effectiveCron = cronOverride ?? existing.Cron;
                if (existing.Enabled && !existing.DeletedByUser)
                {
                    CronExpression? expression = null;
                    try { expression = CronExpression.Parse(effectiveCron, CronFormat.IncludeSeconds); }
                    catch (CronFormatException) { expression = CronExpression.Parse(effectiveCron, CronFormat.Standard); }

                    var timeZone = existing.TimeZoneId is not null
                        ? TimeZoneInfo.FindSystemTimeZoneById(existing.TimeZoneId)
                        : TimeZoneInfo.Utc;

                    var next = expression.GetNextOccurrence(DateTimeOffset.UtcNow, timeZone);
                    if (next.HasValue)
                    {
                        await recurringStorage.SetRecurringJobNextExecutionAsync(recurringId, next.Value, context.RequestAborted).ConfigureAwait(false);
                    }
                }
            }

            LocalRedirect(context, $"{_pathPrefix}/recurring/{Uri.EscapeDataString(recurringId)}", activeCluster);
            return true;
        }

        if (subPath.Contains("/force-delete", StringComparison.Ordinal))
        {
            var recurringId = GetRecurringId(subPath);
            await recurringStorage.ForceDeleteRecurringJobAsync(recurringId, context.RequestAborted).ConfigureAwait(false);
            LocalRedirect(context, $"{_pathPrefix}/recurring", activeCluster);
            return true;
        }

        if (subPath.Contains("/restore", StringComparison.Ordinal))
        {
            var recurringId = GetRecurringId(subPath);
            await recurringStorage.RestoreRecurringJobAsync(recurringId, context.RequestAborted).ConfigureAwait(false);
            LocalRedirect(context, $"{_pathPrefix}/recurring/{Uri.EscapeDataString(recurringId)}", activeCluster);
            return true;
        }

        if (string.Equals(subPath, "recurring/bulk", StringComparison.Ordinal))
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            var action = form["bulkAction"].ToString();
            var ids = form["ids"].ToArray();

            // nothing selected — do nothing
            if (ids.Length == 0)
            {
                LocalRedirect(context, $"{_pathPrefix}/recurring", activeCluster);
                return true;
            }

            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                var decoded = Uri.UnescapeDataString(id);
                if (string.Equals(action, "trigger", StringComparison.Ordinal))
                {
                    await recurringStorage.SetRecurringJobNextExecutionAsync(
                        decoded, DateTimeOffset.UtcNow.AddSeconds(-1), context.RequestAborted).ConfigureAwait(false);
                }
                else if (string.Equals(action, "delete", StringComparison.Ordinal))
                {
                    await recurringStorage.DeleteRecurringJobAsync(decoded, context.RequestAborted).ConfigureAwait(false);
                }
            }

            LocalRedirect(context, $"{_pathPrefix}/recurring", activeCluster);
            return true;
        }

        if (string.Equals(subPath, "recurring/pause-all", StringComparison.Ordinal))
        {
            var rt = await runtimeStore.GetAsync(context.RequestAborted).ConfigureAwait(false);
            rt.RecurringJobsPaused = true;
            await runtimeStore.SaveAsync(rt, context.RequestAborted).ConfigureAwait(false);
            LocalRedirect(context, $"{_pathPrefix}/settings", activeCluster);
            return true;
        }

        if (string.Equals(subPath, "recurring/resume-all", StringComparison.Ordinal))
        {
            var rt = await runtimeStore.GetAsync(context.RequestAborted).ConfigureAwait(false);
            rt.RecurringJobsPaused = false;
            await runtimeStore.SaveAsync(rt, context.RequestAborted).ConfigureAwait(false);
            LocalRedirect(context, $"{_pathPrefix}/settings", activeCluster);
            return true;
        }

        return false;
    }

    private async Task<bool> TryHandleBulkActionAsync(
        HttpContext context, string subPath,
        IDashboardStorage dashboardStorage, IJobControlService controlService,
        DashboardCluster? activeCluster)
    {
        if (!string.Equals(subPath, "jobs/bulk", StringComparison.Ordinal) &&
            !string.Equals(subPath, "jobs/bulk-requeue", StringComparison.Ordinal) &&
            !string.Equals(subPath, "jobs/bulk-delete", StringComparison.Ordinal))
        {
            return false;
        }

        string action;
        string[] ids;

        if (context.Request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            var payload = await context.Request.ReadFromJsonAsync<BulkActionRequest>(context.RequestAborted).ConfigureAwait(false);
            if (payload is null)
            {
                return false;
            }

            action = subPath.EndsWith("-requeue", StringComparison.Ordinal) ? "requeue" : "delete";
            ids = payload.Ids;
        }
        else
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            action = form["bulkAction"].ToString();
            var rawIds = form["ids"].ToArray();
            ids = rawIds.Where(i => i is not null).Select(i => i!).ToArray();
        }

        // if nothing selected (and from form), act on all failed jobs
        if (ids.Length == 0 && string.Equals(action, "requeue", StringComparison.Ordinal))
        {
            var all = await dashboardStorage.GetJobsAsync(
                new JobFilter { Status = JobStatus.Failed }, 1, int.MaxValue, context.RequestAborted).ConfigureAwait(false);
            ids = all.Items.Select(j => j.Id.Value.ToString()).ToArray();
        }

        foreach (var idStr in ids)
        {
            if (!Guid.TryParse(idStr, out var guid))
            {
                continue;
            }

            var jobId = new JobId(guid);
            if (string.Equals(action, "requeue", StringComparison.Ordinal))
            {
                await controlService.RequeueJobAsync(jobId, context.RequestAborted).ConfigureAwait(false);
            }
            else if (string.Equals(action, "delete", StringComparison.Ordinal))
            {
                await controlService.DeleteJobAsync(jobId, context.RequestAborted).ConfigureAwait(false);
            }
        }

        if (context.Request.Headers.ContainsKey("X-Requested-With"))
        {
            context.Response.StatusCode = 200;
            return true;
        }

        LocalRedirect(context, $"{_pathPrefix}/failed", activeCluster);
        return true;
    }

    private async Task<bool> TryHandleSettingsActionAsync(
        HttpContext context, string subPath, IRuntimeSettingsStore runtimeStore,
        IJobControlService controlService, DashboardCluster? activeCluster)
    {
        if (string.Equals(subPath, "settings/workers", StringComparison.Ordinal))
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            if (int.TryParse(form["workers"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var workers) && workers > 0)
            {
                var rt = await runtimeStore.GetAsync(context.RequestAborted).ConfigureAwait(false);
                rt.Workers = workers;
                await runtimeStore.SaveAsync(rt, context.RequestAborted).ConfigureAwait(false);
            }

            LocalRedirect(context, $"{_pathPrefix}/settings", activeCluster);
            return true;
        }

        if (string.Equals(subPath, "settings/polling", StringComparison.Ordinal))
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            if (int.TryParse(form["seconds"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
            {
                var rt = await runtimeStore.GetAsync(context.RequestAborted).ConfigureAwait(false);
                rt.PollingInterval = TimeSpan.FromSeconds(seconds);
                await runtimeStore.SaveAsync(rt, context.RequestAborted).ConfigureAwait(false);
            }

            LocalRedirect(context, $"{_pathPrefix}/settings", activeCluster);
            return true;
        }

        if (string.Equals(subPath, "settings/reset", StringComparison.Ordinal))
        {
            await runtimeStore.SaveAsync(new RuntimeSettings(), context.RequestAborted).ConfigureAwait(false);
            LocalRedirect(context, $"{_pathPrefix}/settings", activeCluster);
            return true;
        }

        if (string.Equals(subPath, "settings/retention", StringComparison.Ordinal))
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);

            var rt = await runtimeStore.GetAsync(context.RequestAborted).ConfigureAwait(false);

            if (int.TryParse(form["retentionSucceededDays"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var succDays) && succDays >= 0)
            {
                rt.RetentionSucceeded = succDays == 0 ? TimeSpan.Zero : TimeSpan.FromDays(succDays);
            }

            if (int.TryParse(form["retentionFailedDays"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var failDays) && failDays >= 0)
            {
                rt.RetentionFailed = failDays == 0 ? TimeSpan.Zero : TimeSpan.FromDays(failDays);
            }

            if (int.TryParse(form["retentionExpiredDays"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expDays) && expDays >= 0)
            {
                rt.RetentionExpired = expDays == 0 ? TimeSpan.Zero : TimeSpan.FromDays(expDays);
            }

            if (int.TryParse(form["retentionDeadLetterDays"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dlDays) && dlDays >= 0)
            {
                rt.RetentionDeadLetter = dlDays == 0 ? TimeSpan.Zero : TimeSpan.FromDays(dlDays);
            }

            await runtimeStore.SaveAsync(rt, context.RequestAborted).ConfigureAwait(false);

            LocalRedirect(context, $"{_pathPrefix}/settings", activeCluster);
            return true;
        }

        if (subPath.StartsWith("queues/", StringComparison.Ordinal) && subPath.EndsWith("/pause", StringComparison.Ordinal))
        {
            var queueName = Uri.UnescapeDataString(subPath.Split('/')[1]);
            await controlService.PauseQueueAsync(queueName, context.RequestAborted).ConfigureAwait(false);
            var referer = context.Request.Headers.Referer.ToString();
            var target = !string.IsNullOrEmpty(referer) && referer.Contains("/queues", StringComparison.Ordinal)
                ? $"{_pathPrefix}/queues"
                : $"{_pathPrefix}/settings";
            LocalRedirect(context, target, activeCluster);
            return true;
        }

        if (subPath.StartsWith("queues/", StringComparison.Ordinal) && subPath.EndsWith("/resume", StringComparison.Ordinal))
        {
            var queueName = Uri.UnescapeDataString(subPath.Split('/')[1]);
            await controlService.ResumeQueueAsync(queueName, context.RequestAborted).ConfigureAwait(false);
            var referer = context.Request.Headers.Referer.ToString();
            var target = !string.IsNullOrEmpty(referer) && referer.Contains("/queues", StringComparison.Ordinal)
                ? $"{_pathPrefix}/queues"
                : $"{_pathPrefix}/settings";
            LocalRedirect(context, target, activeCluster);
            return true;
        }

        return false;
    }

    private async Task<string> RenderPageAsync(HttpContext context, string subPath, DashboardCluster? activeCluster)
    {
#pragma warning disable MA0004
        var dashboardStorage = activeCluster?.DashboardStorage ?? context.RequestServices.GetRequiredService<IDashboardStorage>();
        var jobStorage = activeCluster?.JobStorage ?? (dashboardStorage as IJobStorage) ?? context.RequestServices.GetRequiredService<IJobStorage>();
        var recurringStorage = activeCluster?.RecurringStorage ?? (dashboardStorage as IRecurringStorage) ?? context.RequestServices.GetRequiredService<IRecurringStorage>();
        var cache = context.RequestServices.GetRequiredService<IMemoryCache>();
        await using var renderer = new HtmlRenderer(context.RequestServices,
            context.RequestServices.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());
#pragma warning restore MA0004

        // Compute shared counters once for all pages
        var metrics = await GetCachedMetricsAsync(cache, dashboardStorage, _options, activeCluster, context.RequestAborted).ConfigureAwait(false);
        var servers = await jobStorage.GetActiveServersAsync(TimeSpan.FromMinutes(1), context.RequestAborted).ConfigureAwait(false);
        var queues = await dashboardStorage.GetQueueMetricsAsync(context.RequestAborted).ConfigureAwait(false);
        var nexJobOptions = context.RequestServices.GetRequiredService<NexJobOptions>();

        var effectiveQueues = activeCluster?.Queues ?? _options.Queues;

        // Filter queues when effectiveQueues is specified (queue isolation mode)
        var scopedQueues = effectiveQueues is { Count: > 0 }
            ? queues.Where(q => effectiveQueues.Contains(q.Queue, StringComparer.OrdinalIgnoreCase)).ToList()
            : queues;

        var activeQueues = scopedQueues.Count(q => q.Processing > 0);
        var totalQueues = effectiveQueues is { Count: > 0 }
            ? effectiveQueues.Count
            : nexJobOptions.Queues.Count;

        var listenerRegistry = context.RequestServices.GetService<IListenerRegistry>();
        var allListeners = listenerRegistry?.GetAll() ?? Array.Empty<ListenerSnapshot>();
        var listeningCount = allListeners.Count(l => l.Status == ListenerStatus.Listening);
        var listenersCounter = allListeners.Count > 0 ? $"{listeningCount}/{allListeners.Count}" : null;
        string? listenersClass = null;
        if (allListeners.Count > 0)
        {
            listenersClass = listeningCount == allListeners.Count ? "ok" : "warn";
        }

        NavCounters counters = new NavCounters(
            Queues: $"{activeQueues}/{totalQueues}",
            QueuesClass: activeQueues < totalQueues ? "warn" : "ok",
            Jobs: $"{metrics.Processing}/{metrics.Enqueued}",
            Recurring: $"{metrics.Processing}/{metrics.Recurring}",
            Failed: metrics.Failed > 0 ? metrics.Failed.ToString(CultureInfo.InvariantCulture) : null,
            FailedClass: metrics.Failed > 0 ? "danger" : null,
            Servers: $"{servers.Count}/{servers.Count}",
            ServersClass: "ok",
            Listeners: listenersCounter,
            ListenersClass: listenersClass);

        var clustersList = _options.Clusters.Count > 0 ? _options.Clusters : null;

        ParameterView parameters;

        if (string.Equals(subPath, string.Empty, StringComparison.Ordinal) || string.Equals(subPath, "overview", StringComparison.Ordinal))
        {
            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Storage"] = dashboardStorage,
                ["JobStorage"] = jobStorage,
                ["RecurringStorage"] = recurringStorage,
                ["Listeners"] = allListeners,
                ["PathPrefix"] = _pathPrefix,
                ["Title"] = _options.Title,
                ["Counters"] = counters,
                ["Metrics"] = metrics,
                ["Queues"] = effectiveQueues,
                ["Clusters"] = clustersList,
                ["ActiveCluster"] = activeCluster,
            });
            return await RenderAsync<OverviewPage>(renderer, parameters).ConfigureAwait(false);
        }

        if (string.Equals(subPath, "queues", StringComparison.Ordinal))
        {
            var runtimeStore = activeCluster?.RuntimeStore ?? context.RequestServices.GetService<IRuntimeSettingsStore>();
            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Storage"] = dashboardStorage,
                ["PathPrefix"] = _pathPrefix,
                ["Title"] = _options.Title,
                ["Counters"] = counters,
                ["Options"] = nexJobOptions,
                ["RuntimeStore"] = runtimeStore,
                ["Queues"] = effectiveQueues,
                ["Clusters"] = clustersList,
                ["ActiveCluster"] = activeCluster,
            });
            return await RenderAsync<QueuesPage>(renderer, parameters).ConfigureAwait(false);
        }

        if (string.Equals(subPath, "servers", StringComparison.Ordinal))
        {
            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Storage"] = jobStorage,
                ["PathPrefix"] = _pathPrefix,
                ["Title"] = _options.Title,
                ["Counters"] = counters,
                ["Clusters"] = clustersList,
                ["ActiveCluster"] = activeCluster,
            });
            return await RenderAsync<ServersPage>(renderer, parameters).ConfigureAwait(false);
        }

        if (string.Equals(subPath, "listeners", StringComparison.Ordinal))
        {
            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Listeners"] = allListeners,
                ["PathPrefix"] = _pathPrefix,
                ["Title"] = _options.Title,
                ["Counters"] = counters,
                ["Clusters"] = clustersList,
                ["ActiveCluster"] = activeCluster,
            });
            return await RenderAsync<ListenersPage>(renderer, parameters).ConfigureAwait(false);
        }

        if (string.Equals(subPath, "jobs", StringComparison.Ordinal) || subPath.StartsWith("jobs?", StringComparison.Ordinal))
        {
            var query = context.Request.Query;
            var status = query.TryGetValue("status", out var sv) && Enum.TryParse<JobStatus>(sv, out var s) ? (JobStatus?)s : null;
            var search = query.TryGetValue("search", out var sr) ? (string?)sr : null;
            if (string.IsNullOrWhiteSpace(search) && query.TryGetValue("q", out var qv))
            {
                search = (string?)qv;
            }

            var queue = query.TryGetValue("queue", out var qu) && !string.IsNullOrWhiteSpace(qu) ? (string?)qu : null;
            if (queue is null && effectiveQueues is { Count: 1 })
            {
                queue = effectiveQueues[0];
            }

            var period = query.TryGetValue("period", out var pr) && !string.IsNullOrWhiteSpace(pr) ? (string?)pr : null;
            var tag = query.TryGetValue("tag", out var tg) && !string.IsNullOrWhiteSpace(tg) ? (string?)tg : null;

            if (string.IsNullOrWhiteSpace(tag) && !string.IsNullOrWhiteSpace(search))
            {
                if (search.StartsWith("trigger:", StringComparison.OrdinalIgnoreCase))
                {
                    tag = search.Trim();
                    search = null;
                }
                else if (search.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
                {
                    tag = search[4..].Trim();
                    search = null;
                }
            }

            var page = query.TryGetValue("page", out var pg) && int.TryParse(pg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 1;

            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Storage"] = dashboardStorage,
                ["PathPrefix"] = _pathPrefix,
                ["Title"] = _options.Title,
                ["StatusFilter"] = status,
                ["Search"] = search,
                ["TagFilter"] = tag,
                ["QueueFilter"] = queue,
                ["Period"] = period,
                ["Page"] = page,
                ["Counters"] = counters,
                ["Queues"] = effectiveQueues,
                ["Clusters"] = clustersList,
                ["ActiveCluster"] = activeCluster,
            });
            return await RenderAsync<JobsPage>(renderer, parameters).ConfigureAwait(false);
        }

        if (subPath.StartsWith("jobs/", StringComparison.Ordinal) && subPath.Split('/').Length == 2)
        {
            var idStr = subPath.Split('/')[1];
            if (Guid.TryParse(idStr, out var guid))
            {
                parameters = ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["Storage"] = dashboardStorage,
                    ["PathPrefix"] = _pathPrefix,
                    ["Title"] = _options.Title,
                    ["JobId"] = new JobId(guid),
                    ["Counters"] = counters,
                    ["IsReadOnly"] = activeCluster?.IsReadOnly == true,
                    ["Clusters"] = clustersList,
                    ["ActiveCluster"] = activeCluster,
                });
                return await RenderAsync<JobDetailPage>(renderer, parameters).ConfigureAwait(false);
            }
        }

        if (subPath.StartsWith("recurring/", StringComparison.Ordinal) &&
            subPath.Split('/').Length == 2 &&
            !string.Equals(subPath.Split('/')[1], string.Empty, StringComparison.Ordinal))
        {
            var recurringId = Uri.UnescapeDataString(subPath.Split('/')[1]);
            var recurringJob = await recurringStorage.GetRecurringJobByIdAsync(recurringId, context.RequestAborted).ConfigureAwait(false);
            if (recurringJob is null)
            {
                return HtmlShell.NotFound(_options.Title, _pathPrefix);
            }

            var pageNum = context.Request.Query.TryGetValue("page", out var pg) && int.TryParse(pg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pn) ? pn : 1;
            var pageSize = int.TryParse(context.Request.Query["pageSize"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ps) && (ps == 10 || ps == 20 || ps == 50) ? ps : 20;
            var jobFilter = new JobFilter { RecurringJobId = recurringId };
            var executions = await dashboardStorage.GetJobsAsync(jobFilter, pageNum, pageSize, context.RequestAborted).ConfigureAwait(false);

            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Job"] = recurringJob,
                ["Executions"] = executions,
                ["PageSize"] = pageSize,
                ["PathPrefix"] = _pathPrefix,
                ["Title"] = _options.Title,
                ["Counters"] = counters,
                ["Clusters"] = clustersList,
                ["ActiveCluster"] = activeCluster,
            });
            return await RenderAsync<RecurringJobDetailPage>(renderer, parameters).ConfigureAwait(false);
        }

        if (string.Equals(subPath, "recurring", StringComparison.Ordinal))
        {
            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Storage"] = recurringStorage,
                ["PathPrefix"] = _pathPrefix,
                ["Title"] = _options.Title,
                ["Counters"] = counters,
                ["Clusters"] = clustersList,
                ["ActiveCluster"] = activeCluster,
            });
            return await RenderAsync<RecurringPage>(renderer, parameters).ConfigureAwait(false);
        }

        if (string.Equals(subPath, "failed", StringComparison.Ordinal))
        {
            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Storage"] = dashboardStorage,
                ["PathPrefix"] = _pathPrefix,
                ["Title"] = _options.Title,
                ["Counters"] = counters,
                ["Queues"] = effectiveQueues,
                ["Clusters"] = clustersList,
                ["ActiveCluster"] = activeCluster,
            });
            return await RenderAsync<FailedPage>(renderer, parameters).ConfigureAwait(false);
        }

        if (string.Equals(subPath, "settings", StringComparison.Ordinal))
        {
            var runtimeStore = activeCluster?.RuntimeStore
                ?? context.RequestServices.GetService<IRuntimeSettingsStore>()
                ?? context.RequestServices.GetRequiredService<IRuntimeSettingsStore>();
            var runtime = await runtimeStore.GetAsync(context.RequestAborted).ConfigureAwait(false);

            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["RuntimeStore"] = runtimeStore,
                ["Options"] = nexJobOptions,
                ["Runtime"] = runtime,
                ["PathPrefix"] = _pathPrefix,
                ["Title"] = _options.Title,
                ["Counters"] = counters,
                ["Clusters"] = clustersList,
                ["ActiveCluster"] = activeCluster,
            });
            return await RenderAsync<SettingsPage>(renderer, parameters).ConfigureAwait(false);
        }

        return HtmlShell.NotFound(_options.Title, _pathPrefix);
    }

    /// <summary>Request payload for bulk job actions.</summary>
    /// <param name="Ids">The job IDs to process.</param>
    private sealed record BulkActionRequest(string[] Ids);
}
