using Cronos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
        _next       = next;
        _pathPrefix = pathPrefix.TrimEnd('/');
        _options    = options;
    }

    /// <summary>Processes an incoming request.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (!path.StartsWith(_pathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (_options.RequireAuth && context.User?.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = 401;
            return;
        }

        var subPath = path[_pathPrefix.Length..].TrimStart('/');

        // SSE metrics stream
        if (subPath == "stream" && context.Request.Method == HttpMethods.Get)
        {
            var storage = context.RequestServices.GetRequiredService<IStorageProvider>();
            await DashboardStreamEndpoint.HandleAsync(context, storage);
            return;
        }

        // Handle API actions (POST)
        if (context.Request.Method == HttpMethods.Post && await HandleActionsAsync(context, subPath))
            return;

        // Render page
        var html = await RenderPageAsync(context, subPath);
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html);
    }

    private static async Task<string> RenderAsync<TComponent>(
        HtmlRenderer renderer, ParameterView parameters)
        where TComponent : IComponent
    {
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(parameters);
            return output.ToHtmlString();
        });
    }

    private async Task<bool> HandleActionsAsync(HttpContext context, string subPath)
    {
        var storage = context.RequestServices.GetRequiredService<IStorageProvider>();

        if (subPath.StartsWith("jobs/") && subPath.Contains("/runnow"))
        {
            var idStr = subPath.Split('/')[1];
            if (Guid.TryParse(idStr, out var guid))
            {
                await storage.RequeueJobAsync(new JobId(guid), context.RequestAborted);
                context.Response.Redirect($"{_pathPrefix}/jobs/{guid}");
                return true;
            }
        }

        if (subPath.StartsWith("jobs/") && subPath.Contains("/requeue"))
        {
            var idStr = subPath.Split('/')[1];
            if (Guid.TryParse(idStr, out var guid))
            {
                await storage.RequeueJobAsync(new JobId(guid), context.RequestAborted);
                context.Response.Redirect($"{_pathPrefix}/failed");
                return true;
            }
        }

        if (subPath.StartsWith("jobs/") && subPath.Contains("/delete"))
        {
            var idStr = subPath.Split('/')[1];
            if (Guid.TryParse(idStr, out var guid))
            {
                await storage.DeleteJobAsync(new JobId(guid), context.RequestAborted);
                context.Response.Redirect($"{_pathPrefix}/failed");
                return true;
            }
        }

        if (subPath.StartsWith("recurring/") && subPath.Contains("/delete"))
        {
            var recurringId = Uri.UnescapeDataString(subPath.Split('/')[1]);
            await storage.DeleteRecurringJobAsync(recurringId, context.RequestAborted);
            context.Response.Redirect($"{_pathPrefix}/recurring");
            return true;
        }

        if (subPath.StartsWith("recurring/") && subPath.Contains("/trigger"))
        {
            var recurringId = Uri.UnescapeDataString(subPath.Split('/')[1]);
            await storage.SetRecurringJobNextExecutionAsync(
                recurringId, DateTimeOffset.UtcNow.AddSeconds(-1), context.RequestAborted);
            context.Response.Redirect($"{_pathPrefix}/recurring");
            return true;
        }

        if (subPath.StartsWith("recurring/") && subPath.Contains("/pause"))
        {
            var recurringId = Uri.UnescapeDataString(subPath.Split('/')[1]);
            var allJobs = await storage.GetRecurringJobsAsync(context.RequestAborted);
            var existing = allJobs.FirstOrDefault(r => r.RecurringJobId == recurringId);
            if (existing is not null)
                await storage.UpdateRecurringJobConfigAsync(recurringId, existing.CronOverride, enabled: false, context.RequestAborted);
            context.Response.Redirect($"{_pathPrefix}/recurring");
            return true;
        }

        if (subPath.StartsWith("recurring/") && subPath.Contains("/resume"))
        {
            var recurringId = Uri.UnescapeDataString(subPath.Split('/')[1]);
            var allJobs = await storage.GetRecurringJobsAsync(context.RequestAborted);
            var existing = allJobs.FirstOrDefault(r => r.RecurringJobId == recurringId);
            if (existing is not null)
                await storage.UpdateRecurringJobConfigAsync(recurringId, existing.CronOverride, enabled: true, context.RequestAborted);
            context.Response.Redirect($"{_pathPrefix}/recurring");
            return true;
        }

        if (subPath.StartsWith("recurring/") && subPath.Contains("/update-config"))
        {
            var recurringId = Uri.UnescapeDataString(subPath.Split('/')[1]);
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
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
                    context.Response.Redirect($"{_pathPrefix}/recurring");
                    return true;
                }
            }

            var allJobs = await storage.GetRecurringJobsAsync(context.RequestAborted);
            var existing = allJobs.FirstOrDefault(r => r.RecurringJobId == recurringId);
            if (existing is not null)
                await storage.UpdateRecurringJobConfigAsync(recurringId, cronOverride, existing.Enabled, context.RequestAborted);

            context.Response.Redirect($"{_pathPrefix}/recurring");
            return true;
        }

        if (subPath.StartsWith("recurring/") && subPath.Contains("/force-delete"))
        {
            var recurringId = Uri.UnescapeDataString(subPath.Split('/')[1]);
            await storage.ForceDeleteRecurringJobAsync(recurringId, context.RequestAborted);
            context.Response.Redirect($"{_pathPrefix}/recurring");
            return true;
        }

        if (subPath.StartsWith("recurring/") && subPath.Contains("/restore"))
        {
            var recurringId = Uri.UnescapeDataString(subPath.Split('/')[1]);
            await storage.RestoreRecurringJobAsync(recurringId, context.RequestAborted);
            context.Response.Redirect($"{_pathPrefix}/recurring");
            return true;
        }

        // ── Bulk actions ──────────────────────────────────────────────────────

        if (subPath == "recurring/bulk")
        {
            var form   = await context.Request.ReadFormAsync(context.RequestAborted);
            var action = form["bulkAction"].ToString();
            var ids    = form["ids"].ToArray();

            // if nothing selected, act on all
            if (ids.Length == 0)
                ids = (await storage.GetRecurringJobsAsync(context.RequestAborted))
                      .Select(r => r.RecurringJobId).ToArray();

            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id)) continue;
                var decoded = Uri.UnescapeDataString(id);
                if (action == "trigger")
                    await storage.SetRecurringJobNextExecutionAsync(
                        decoded, DateTimeOffset.UtcNow.AddSeconds(-1), context.RequestAborted);
                else if (action == "delete")
                    await storage.DeleteRecurringJobAsync(decoded, context.RequestAborted);
            }

            context.Response.Redirect($"{_pathPrefix}/recurring");
            return true;
        }

        if (subPath == "jobs/bulk")
        {
            var form   = await context.Request.ReadFormAsync(context.RequestAborted);
            var action = form["bulkAction"].ToString();
            var ids    = form["ids"].ToArray();

            // if nothing selected, act on all failed jobs
            if (ids.Length == 0)
            {
                var all = await storage.GetJobsAsync(
                    new JobFilter { Status = JobStatus.Failed }, 1, int.MaxValue, context.RequestAborted);
                ids = all.Items.Select(j => j.Id.Value.ToString()).ToArray();
            }

            foreach (var idStr in ids)
            {
                if (!Guid.TryParse(idStr, out var guid)) continue;
                var jobId = new JobId(guid);
                if (action == "requeue")
                    await storage.RequeueJobAsync(jobId, context.RequestAborted);
                else if (action == "delete")
                    await storage.DeleteJobAsync(jobId, context.RequestAborted);
            }

            context.Response.Redirect($"{_pathPrefix}/failed");
            return true;
        }

        return false;
    }

    private async Task<string> RenderPageAsync(HttpContext context, string subPath)
    {
        var storage = context.RequestServices.GetRequiredService<IStorageProvider>();
        await using var renderer = new HtmlRenderer(context.RequestServices,
            context.RequestServices.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>());

        ParameterView parameters;

        if (subPath == string.Empty || subPath == "overview")
        {
            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Storage"]     = storage,
                ["PathPrefix"]  = _pathPrefix,
                ["Title"]       = _options.Title,
            });
            return await RenderAsync<OverviewPage>(renderer, parameters);
        }

        if (subPath == "queues")
        {
            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Storage"]    = storage,
                ["PathPrefix"] = _pathPrefix,
                ["Title"]      = _options.Title,
            });
            return await RenderAsync<QueuesPage>(renderer, parameters);
        }

        if (subPath == "jobs" || subPath.StartsWith("jobs?"))
        {
            var query  = context.Request.Query;
            var status = query.TryGetValue("status", out var sv) && Enum.TryParse<JobStatus>(sv, out var s) ? (JobStatus?)s : null;
            var search = query.TryGetValue("search", out var sr) ? (string?)sr : null;
            var page   = query.TryGetValue("page",   out var pg) && int.TryParse(pg, out var p) ? p : 1;

            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Storage"]      = storage,
                ["PathPrefix"]   = _pathPrefix,
                ["Title"]        = _options.Title,
                ["StatusFilter"] = status,
                ["Search"]       = search,
                ["Page"]         = page,
            });
            return await RenderAsync<JobsPage>(renderer, parameters);
        }

        if (subPath.StartsWith("jobs/") && subPath.Split('/').Length == 2)
        {
            var idStr = subPath.Split('/')[1];
            if (Guid.TryParse(idStr, out var guid))
            {
                parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["Storage"]    = storage,
                    ["PathPrefix"] = _pathPrefix,
                    ["Title"]      = _options.Title,
                    ["JobId"]      = new JobId(guid),
                });
                return await RenderAsync<JobDetailPage>(renderer, parameters);
            }
        }

        if (subPath == "recurring")
        {
            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Storage"]    = storage,
                ["PathPrefix"] = _pathPrefix,
                ["Title"]      = _options.Title,
            });
            return await RenderAsync<RecurringPage>(renderer, parameters);
        }

        if (subPath == "failed")
        {
            parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Storage"]    = storage,
                ["PathPrefix"] = _pathPrefix,
                ["Title"]      = _options.Title,
            });
            return await RenderAsync<FailedPage>(renderer, parameters);
        }

        return HtmlShell.NotFound(_options.Title, _pathPrefix);
    }
}
