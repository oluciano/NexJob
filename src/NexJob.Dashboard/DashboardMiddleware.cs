using System.Globalization;
using Cronos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
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

        if (_options.RequireAuth && context.User?.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = 401;
            return;
        }

        var subPath = path[_pathPrefix.Length..].TrimStart('/');

        // SSE metrics stream
        if (string.Equals(subPath, "stream", StringComparison.Ordinal) && string.Equals(context.Request.Method, HttpMethods.Get, StringComparison.Ordinal))
        {
            var storage = context.RequestServices.GetRequiredService<IStorageProvider>();
            await DashboardStreamEndpoint.HandleAsync(context, storage).ConfigureAwait(false);
            return;
        }

        // GET /jobs/{id}/logs — returns execution logs as JSON for modal
        var logsSegments = subPath.Split('/');
        if (string.Equals(context.Request.Method, HttpMethods.Get, StringComparison.Ordinal) &&
            logsSegments.Length == 3 &&
            string.Equals(logsSegments[0], "jobs", StringComparison.Ordinal) &&
            string.Equals(logsSegments[2], "logs", StringComparison.Ordinal))
        {
            var logsStorage = context.RequestServices.GetRequiredService<IStorageProvider>();
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
        if (string.Equals(context.Request.Method, HttpMethods.Post, StringComparison.Ordinal) && await HandleActionsAsync(context, subPath).ConfigureAwait(false))
        {
            return;
        }

        // Render page
        var html = await RenderPageAsync(context, subPath).ConfigureAwait(false);
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html).ConfigureAwait(false);
    }

    private static async Task<string> RenderAsync<TComponent>(
        HtmlRenderer renderer, ParameterView parameters)
        where TComponent : IComponent
#pragma warning disable MA0004
    {
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(parameters).ConfigureAwait(false);
            return output.ToHtmlString();
#pragma warning restore MA0004
        }).ConfigureAwait(false);
    }

#pragma warning disable SCS0027
    private static void LocalRedirect(HttpContext context, string location)
    {
        if (location.StartsWith("/", StringComparison.Ordinal) && !location.StartsWith("//", StringComparison.Ordinal))
        {
            context.Response.Redirect(location);
        }
    }
#pragma warning restore SCS0027

    private async Task<bool> HandleActionsAsync(HttpContext context, string subPath)
    {
        var storage = context.RequestServices.GetRequiredService<IStorageProvider>();

        if (subPath.StartsWith("jobs/", StringComparison.Ordinal) && subPath.Contains("/runnow", StringComparison.Ordinal))
        {
            var idStr = subPath.Split('/')[1];
            if (Guid.TryParse(idStr, out var guid))
            {
                await storage.RequeueJobAsync(new JobId(guid), context.RequestAborted).ConfigureAwait(false);
                LocalRedirect(context, $"{_pathPrefix}/jobs/{guid}");
                return true;
            }
        }

        if (subPath.StartsWith("jobs/", StringComparison.Ordinal) && subPath.Contains("/requeue", StringComparison.Ordinal))
        {
            var idStr = subPath.Split('/')[1];
            if (Guid.TryParse(idStr, out var guid))
            {
                await storage.RequeueJobAsync(new JobId(guid), context.RequestAborted).ConfigureAwait(false);
                LocalRedirect(context, $"{_pathPrefix}/failed");
                return true;
            }
        }

        if (subPath.StartsWith("jobs/", StringComparison.Ordinal) && subPath.Contains("/delete", StringComparison.Ordinal))
        {
            var idStr = subPath.Split('/')[1];
            if (Guid.TryParse(idStr, out var guid))
            {
                await storage.DeleteJobAsync(new JobId(guid), context.RequestAborted).ConfigureAwait(false);
                LocalRedirect(context, $"{_pathPrefix}/failed");
                return true;
            }
        }

        if (subPath.StartsWith("recurring/", StringComparison.Ordinal) && subPath.Contains("/delete", StringComparison.Ordinal))
        {
            var recurringId = Uri.UnescapeDataString(subPath.Split('/')[1]);
            await storage.DeleteRecurringJobAsync(recurringId, context.RequestAborted).ConfigureAwait(false);
            LocalRedirect(context, $"{_pathPrefix}/recurring");
            return true;
        }

        if (subPath.StartsWith("recurring/", StringComparison.Ordinal) && subPath.Contains("/trigger", StringComparison.Ordinal))
        {
            var recurringId = Uri.UnescapeDataString(subPath.Split('/')[1]);
            await storage.SetRecurringJobNextExecutionAsync(
                recurringId, DateTimeOffset.UtcNow.AddSeconds(-1), context.RequestAborted).ConfigureAwait(false);
            LocalRedirect(context, $"{_pathPrefix}/recurring/{Uri.UnescapeDataString(recurringId)}");
            return true;
        }

        if (subPath.StartsWith("recurring/", StringComparison.Ordinal) && subPath.Contains("/pause", StringComparison.Ordinal))
        {
            var recurringId = Uri.UnescapeDataString(subPath.Split('/')[1]);
            var allJobs = await storage.GetRecurringJobsAsync(context.RequestAborted).ConfigureAwait(false);
            var existing = allJobs.FirstOrDefault(r => string.Equals(r.RecurringJobId, recurringId, StringComparison.Ordinal));
            if (existing is not null)
            {
                await storage.UpdateRecurringJobConfigAsync(recurringId, existing.CronOverride, enabled: false, context.RequestAborted).ConfigureAwait(false);
            }

            LocalRedirect(context, $"{_pathPrefix}/recurring/{Uri.UnescapeDataString(recurringId)}");
            return true;
        }

        if (subPath.StartsWith("recurring/", StringComparison.Ordinal) && subPath.Contains("/resume", StringComparison.Ordinal))
        {
            var recurringId = Uri.UnescapeDataString(subPath.Split('/')[1]);
            var allJobs = await storage.GetRecurringJobsAsync(context.RequestAborted).ConfigureAwait(false);
            var existing = allJobs.FirstOrDefault(r => string.Equals(r.RecurringJobId, recurringId, StringComparison.Ordinal));
            if (existing is not null)
            {
                await storage.UpdateRecurringJobConfigAsync(recurringId, existing.CronOverride, enabled: true, context.RequestAborted).ConfigureAwait(false);
            }

            LocalRedirect(context, $"{_pathPrefix}/recurring/{Uri.UnescapeDataString(recurringId)}");
            return true;
        }

        if (subPath.StartsWith("recurring/", StringComparison.Ordinal) && subPath.Contains("/update-config", StringComparison.Ordinal))
        {
            var recurringId = Uri.UnescapeDataString(subPath.Split('/')[1]);
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
                    LocalRedirect(context, $"{_pathPrefix}/recurring/{Uri.EscapeDataString(recurringId)}");
                    return true;
                }
            }

            var allJobs = await storage.GetRecurringJobsAsync(context.RequestAborted).ConfigureAwait(false);
            var existing = allJobs.FirstOrDefault(r => string.Equals(r.RecurringJobId, recurringId, StringComparison.Ordinal));
            if (existing is not null)
            {
                await storage.UpdateRecurringJobConfigAsync(recurringId, cronOverride, existing.Enabled, context.RequestAborted).ConfigureAwait(false);

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
                        await storage.SetRecurringJobNextExecutionAsync(recurringId, next.Value, context.RequestAborted).ConfigureAwait(false);
                    }
                }
            }

            LocalRedirect(context, $"{_pathPrefix}/recurring/{Uri.EscapeDataString(recurringId)}");
            return true;
        }

        if (subPath.StartsWith("recurring/", StringComparison.Ordinal) && subPath.Contains("/force-delete", StringComparison.Ordinal))
        {
            var recurringId = Uri.UnescapeDataString(subPath.Split('/')[1]);
            await storage.ForceDeleteRecurringJobAsync(recurringId, context.RequestAborted).ConfigureAwait(false);
            LocalRedirect(context, $"{_pathPrefix}/recurring");
            return true;
        }

        if (subPath.StartsWith("recurring/", StringComparison.Ordinal) && subPath.Contains("/restore", StringComparison.Ordinal))
        {
            var recurringId = Uri.UnescapeDataString(subPath.Split('/')[1]);
            await storage.RestoreRecurringJobAsync(recurringId, context.RequestAborted).ConfigureAwait(false);
            LocalRedirect(context, $"{_pathPrefix}/recurring/{Uri.EscapeDataString(recurringId)}");
            return true;
        }

        // ── Bulk actions ──────────────────────────────────────────────────────

        if (string.Equals(subPath, "recurring/bulk", StringComparison.Ordinal))
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            var action = form["bulkAction"].ToString();
            var ids = form["ids"].ToArray();

            // nothing selected — do nothing
            if (ids.Length == 0)
            {
                LocalRedirect(context, $"{_pathPrefix}/recurring");
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
                    await storage.SetRecurringJobNextExecutionAsync(
                        decoded, DateTimeOffset.UtcNow.AddSeconds(-1), context.RequestAborted).ConfigureAwait(false);
                }
                else if (string.Equals(action, "delete", StringComparison.Ordinal))
                {
                    await storage.DeleteRecurringJobAsync(decoded, context.RequestAborted).ConfigureAwait(false);
                }
            }

            LocalRedirect(context, $"{_pathPrefix}/recurring");
            return true;
        }

        if (string.Equals(subPath, "jobs/bulk", StringComparison.Ordinal))
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            var action = form["bulkAction"].ToString();
            var ids = form["ids"].ToArray();

            // if nothing selected, act on all failed jobs
            if (ids.Length == 0)
            {
                var all = await storage.GetJobsAsync(
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
                    await storage.RequeueJobAsync(jobId, context.RequestAborted).ConfigureAwait(false);
                }
                else if (string.Equals(action, "delete", StringComparison.Ordinal))
                {
                    await storage.DeleteJobAsync(jobId, context.RequestAborted).ConfigureAwait(false);
                }
            }

            LocalRedirect(context, $"{_pathPrefix}/failed");
            return true;
        }

        // ── Settings live-config actions ──────────────────────────────────────

        var runtimeStore = context.RequestServices.GetRequiredService<IRuntimeSettingsStore>();

        if (string.Equals(subPath, "settings/workers", StringComparison.Ordinal))
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted).ConfigureAwait(false);
            if (int.TryParse(form["workers"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var workers) && workers > 0)
            {
                var rt = await runtimeStore.GetAsync(context.RequestAborted).ConfigureAwait(false);
                rt.Workers = workers;
                await runtimeStore.SaveAsync(rt, context.RequestAborted).ConfigureAwait(false);
            }

            LocalRedirect(context, $"{_pathPrefix}/settings");
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

            LocalRedirect(context, $"{_pathPrefix}/settings");
            return true;
        }

        if (string.Equals(subPath, "settings/reset", StringComparison.Ordinal))
        {
            await runtimeStore.SaveAsync(new RuntimeSettings(), context.RequestAborted).ConfigureAwait(false);
            LocalRedirect(context, $"{_pathPrefix}/settings");
            return true;
        }

        if (subPath.StartsWith("queues/", StringComparison.Ordinal) && subPath.EndsWith("/pause", StringComparison.Ordinal))
        {
            var queueName = Uri.UnescapeDataString(subPath.Split('/')[1]);
            var rt = await runtimeStore.GetAsync(context.RequestAborted).ConfigureAwait(false);
            rt.PausedQueues.Add(queueName);
            await runtimeStore.SaveAsync(rt, context.RequestAborted).ConfigureAwait(false);
            LocalRedirect(context, $"{_pathPrefix}/settings");
            return true;
        }

        if (subPath.StartsWith("queues/", StringComparison.Ordinal) && subPath.EndsWith("/resume", StringComparison.Ordinal))
        {
            var queueName = Uri.UnescapeDataString(subPath.Split('/')[1]);
            var rt = await runtimeStore.GetAsync(context.RequestAborted).ConfigureAwait(false);
            rt.PausedQueues.Remove(queueName);
            await runtimeStore.SaveAsync(rt, context.RequestAborted).ConfigureAwait(false);
            LocalRedirect(context, $"{_pathPrefix}/settings");
            return true;
        }

        if (string.Equals(subPath, "recurring/pause-all", StringComparison.Ordinal))
        {
            var rt = await runtimeStore.GetAsync(context.RequestAborted).ConfigureAwait(false);
            rt.RecurringJobsPaused = true;
            await runtimeStore.SaveAsync(rt, context.RequestAborted).ConfigureAwait(false);
            LocalRedirect(context, $"{_pathPrefix}/settings");
            return true;
        }

        if (string.Equals(subPath, "recurring/resume-all", StringComparison.Ordinal))
        {
            var rt = await runtimeStore.GetAsync(context.RequestAborted).ConfigureAwait(false);
            rt.RecurringJobsPaused = false;
            await runtimeStore.SaveAsync(rt, context.RequestAborted).ConfigureAwait(false);
            LocalRedirect(context, $"{_pathPrefix}/settings");
            return true;
        }

        return false;
    }

    private async Task<string> RenderPageAsync(HttpContext context, string subPath)
    {
#pragma warning disable MA0004
        var storage = context.RequestServices.GetRequiredService<IStorageProvider>();
        await using var renderer = new HtmlRenderer(context.RequestServices,
            context.RequestServices.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());
#pragma warning restore MA0004

        // Compute shared counters once for all pages
        var metrics = await storage.GetMetricsAsync().ConfigureAwait(false);
        var servers = await storage.GetActiveServersAsync(TimeSpan.FromMinutes(1), context.RequestAborted).ConfigureAwait(false);
        var queues = await storage.GetQueueMetricsAsync(context.RequestAborted).ConfigureAwait(false);
        var nexJobOptions = context.RequestServices.GetRequiredService<NexJobOptions>();

        var activeQueues = queues.Count(q => q.Processing > 0);
        var totalQueues = nexJobOptions.Queues.Count;
        NavCounters counters = new NavCounters(
            Queues: $"{activeQueues}/{totalQueues}",
            QueuesClass: activeQueues < totalQueues ? "warn" : "ok",
            Jobs: $"{metrics.Processing}/{metrics.Enqueued}",
            Recurring: $"{metrics.Processing}/{metrics.Recurring}",
            Failed: metrics.Failed > 0 ? metrics.Failed.ToString(CultureInfo.InvariantCulture) : null,
            FailedClass: metrics.Failed > 0 ? "danger" : null,
            Servers: $"{servers.Count}/{servers.Count}",
            ServersClass: "ok");

        ParameterView parameters;

        if (string.Equals(subPath, string.Empty, StringComparison.Ordinal) || string.Equals(subPath, "overview", StringComparison.Ordinal))
        {
            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Storage"] = storage,
                ["PathPrefix"] = _pathPrefix,
                ["Title"] = _options.Title,
                ["Counters"] = counters,
                ["Metrics"] = metrics,
            });
            return await RenderAsync<OverviewPage>(renderer, parameters).ConfigureAwait(false);
        }

        if (string.Equals(subPath, "queues", StringComparison.Ordinal))
        {
            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Storage"] = storage,
                ["PathPrefix"] = _pathPrefix,
                ["Title"] = _options.Title,
                ["Counters"] = counters,
                ["Options"] = nexJobOptions,
            });
            return await RenderAsync<QueuesPage>(renderer, parameters).ConfigureAwait(false);
        }

        if (string.Equals(subPath, "servers", StringComparison.Ordinal))
        {
            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Storage"] = storage,
                ["PathPrefix"] = _pathPrefix,
                ["Title"] = _options.Title,
                ["Counters"] = counters,
            });
            return await RenderAsync<ServersPage>(renderer, parameters).ConfigureAwait(false);
        }

        if (string.Equals(subPath, "jobs", StringComparison.Ordinal) || subPath.StartsWith("jobs?", StringComparison.Ordinal))
        {
            var query = context.Request.Query;
            var status = query.TryGetValue("status", out var sv) && Enum.TryParse<JobStatus>(sv, out var s) ? (JobStatus?)s : null;
            var search = query.TryGetValue("search", out var sr) ? (string?)sr : null;
            var tag = query.TryGetValue("tag", out var tg) && !string.IsNullOrWhiteSpace(tg) ? (string?)tg : null;
            var page = query.TryGetValue("page", out var pg) && int.TryParse(pg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 1;

            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Storage"] = storage,
                ["PathPrefix"] = _pathPrefix,
                ["Title"] = _options.Title,
                ["StatusFilter"] = status,
                ["Search"] = search,
                ["TagFilter"] = tag,
                ["Page"] = page,
                ["Counters"] = counters,
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
                    ["Storage"] = storage,
                    ["PathPrefix"] = _pathPrefix,
                    ["Title"] = _options.Title,
                    ["JobId"] = new JobId(guid),
                    ["Counters"] = counters,
                });
                return await RenderAsync<JobDetailPage>(renderer, parameters).ConfigureAwait(false);
            }
        }

        if (subPath.StartsWith("recurring/", StringComparison.Ordinal) &&
            subPath.Split('/').Length == 2 &&
            !string.Equals(subPath.Split('/')[1], string.Empty, StringComparison.Ordinal))
        {
            var recurringId = Uri.UnescapeDataString(subPath.Split('/')[1]);
            var recurringJob = await storage.GetRecurringJobByIdAsync(recurringId, context.RequestAborted).ConfigureAwait(false);
            if (recurringJob is null)
            {
                return HtmlShell.NotFound(_options.Title, _pathPrefix);
            }

            var pageNum = context.Request.Query.TryGetValue("page", out var pg) && int.TryParse(pg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pn) ? pn : 1;
            var pageSize = int.TryParse(context.Request.Query["pageSize"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ps) && (ps == 10 || ps == 20 || ps == 50) ? ps : 20;
            var jobFilter = new JobFilter { RecurringJobId = recurringId };
            var executions = await storage.GetJobsAsync(jobFilter, pageNum, pageSize, context.RequestAborted).ConfigureAwait(false);

            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Job"] = recurringJob,
                ["Executions"] = executions,
                ["PageSize"] = pageSize,
                ["PathPrefix"] = _pathPrefix,
                ["Title"] = _options.Title,
                ["Counters"] = counters,
            });
            return await RenderAsync<RecurringJobDetailPage>(renderer, parameters).ConfigureAwait(false);
        }

        if (string.Equals(subPath, "recurring", StringComparison.Ordinal))
        {
            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Storage"] = storage,
                ["PathPrefix"] = _pathPrefix,
                ["Title"] = _options.Title,
                ["Counters"] = counters,
            });
            return await RenderAsync<RecurringPage>(renderer, parameters).ConfigureAwait(false);
        }

        if (string.Equals(subPath, "failed", StringComparison.Ordinal))
        {
            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Storage"] = storage,
                ["PathPrefix"] = _pathPrefix,
                ["Title"] = _options.Title,
                ["Counters"] = counters,
            });
            return await RenderAsync<FailedPage>(renderer, parameters).ConfigureAwait(false);
        }

        if (string.Equals(subPath, "settings", StringComparison.Ordinal))
        {
            var runtimeStore = context.RequestServices.GetRequiredService<IRuntimeSettingsStore>();
            var runtime = await runtimeStore.GetAsync(context.RequestAborted).ConfigureAwait(false);

            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["RuntimeStore"] = runtimeStore,
                ["Options"] = nexJobOptions,
                ["Runtime"] = runtime,
                ["PathPrefix"] = _pathPrefix,
                ["Title"] = _options.Title,
                ["Counters"] = counters,
            });
            return await RenderAsync<SettingsPage>(renderer, parameters).ConfigureAwait(false);
        }

        return HtmlShell.NotFound(_options.Title, _pathPrefix);
    }
}
