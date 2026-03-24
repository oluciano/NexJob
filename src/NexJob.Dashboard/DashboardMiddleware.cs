using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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
        var path = context.Request.Path.Value ?? "";

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

        // Handle API actions (POST)
        if (context.Request.Method == HttpMethods.Post && await HandleActionsAsync(context, subPath))
            return;

        // Render page
        var html = await RenderPageAsync(context, subPath);
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html);
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
            // Trigger is handled by the page itself; redirect back
            context.Response.Redirect($"{_pathPrefix}/recurring");
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

        if (subPath == "" || subPath == "overview")
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
}

/// <summary>
/// Extension methods for adding the NexJob dashboard to the middleware pipeline.
/// </summary>
public static class DashboardApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the NexJob dashboard middleware at the specified path prefix.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="pathPrefix">URL prefix where the dashboard is mounted (e.g. <c>/jobs</c>).</param>
    /// <param name="configure">Optional delegate to customise <see cref="DashboardOptions"/>.</param>
    public static IApplicationBuilder UseNexJobDashboard(
        this IApplicationBuilder app,
        string pathPrefix = "/jobs",
        Action<DashboardOptions>? configure = null)
    {
        var options = new DashboardOptions();
        configure?.Invoke(options);
        return app.UseMiddleware<DashboardMiddleware>(pathPrefix, options);
    }
}
