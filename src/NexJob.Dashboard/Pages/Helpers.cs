using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

[ExcludeFromCodeCoverage]
internal static class Helpers
{
    internal static async Task<JobMetrics> GetCachedMetricsAsync(
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

    internal static string ShortType(string fullType)
    {
        var name = fullType.Split(',')[0]; // remove assembly part
        var parts = name.Split('.');
        return parts[^1];
    }

    internal static Type? ResolveType(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
        {
            return null;
        }

        var type = Type.GetType(typeName, throwOnError: false);
        if (type is not null)
        {
            return type;
        }

        var cleanName = typeName.Split(',')[0].Trim();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(typeName, throwOnError: false) ?? assembly.GetType(cleanName, throwOnError: false);
            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }

    private static readonly int FrameworkDefaultMaxAttempts = new NexJobOptions().MaxAttempts;

    internal static int GetEffectiveMaxAttempts(JobRecord job)
    {
        if (job is null)
        {
            return 1;
        }

        // Same rule as the retry policy. The dashboard does not see the host's NexJobOptions, so the framework default
        // stands in for the global default: a stored limit different from it is a per-job choice and wins.
        if (job.MaxAttempts != FrameworkDefaultMaxAttempts)
        {
            return job.MaxAttempts;
        }

        var jobType = ResolveType(job.JobType);
        var retryAttr = jobType?.GetCustomAttribute<RetryAttribute>(inherit: true);
        return retryAttr?.Attempts ?? job.MaxAttempts;
    }

    internal static bool IsParameterlessJob(string typeName)
    {
        var type = ResolveType(typeName);
        return type is not null && typeof(IJob).IsAssignableFrom(type);
    }

    internal static Type? ResolveJobInputType(string typeName)
    {
        var type = ResolveType(typeName);
        if (type is null)
        {
            return null;
        }

        var jobInterface = Array.Find(
            type.GetInterfaces(),
            i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IJob<>));
        return jobInterface?.GetGenericArguments()[0];
    }

    internal static string GenerateDefaultJsonSchema(Type inputType)
    {
        try
        {
            var obj = CreateSampleInstance(inputType);
            return System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
            });
        }
        catch
        {
            return "{}";
        }
    }

    private static object? CreateSampleInstance(Type type)
    {
        if (type == typeof(string))
        {
            return "string";
        }

        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
        {
            return 0;
        }

        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
        {
            return 0.0;
        }

        if (type == typeof(bool))
        {
            return true;
        }

        if (type == typeof(Guid))
        {
            return Guid.NewGuid();
        }

        if (type == typeof(DateTime))
        {
            return DateTime.UtcNow;
        }

        if (type == typeof(DateTimeOffset))
        {
            return DateTimeOffset.UtcNow;
        }

        if (type.IsEnum)
        {
            return Enum.GetValues(type).GetValue(0);
        }

        try
        {
            var ctors = type.GetConstructors();
            if (ctors.Length > 0)
            {
                var ctor = ctors.OrderByDescending(c => c.GetParameters().Length).First();
                var parameters = ctor.GetParameters();
                if (parameters.Length > 0)
                {
                    var args = new object?[parameters.Length];
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        args[i] = CreateSampleInstance(parameters[i].ParameterType);
                    }

                    return ctor.Invoke(args);
                }
            }

            return Activator.CreateInstance(type);
        }
        catch
        {
            // If instantiation fails, build a dictionary from public writable properties or constructor parameters
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            if (ctor is not null)
            {
                foreach (var p in ctor.GetParameters())
                {
                    dict[p.Name ?? "property"] = CreateSampleInstance(p.ParameterType);
                }
            }

            foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (prop.CanWrite && !dict.ContainsKey(prop.Name))
                {
                    dict[prop.Name] = CreateSampleInstance(prop.PropertyType);
                }
            }

            return dict.Count > 0 ? dict : null;
        }
    }

    internal static string BadgeHtml(JobStatus s) => s switch
    {
        JobStatus.Enqueued => "<span class=\"badge badge-enqueued\">Enqueued</span>",
        JobStatus.Processing => "<span class=\"badge badge-processing\">Processing</span>",
        JobStatus.Succeeded => "<span class=\"badge badge-succeeded\">Succeeded</span>",
        JobStatus.Failed => "<span class=\"badge badge-failed\">Failed</span>",
        JobStatus.Scheduled => "<span class=\"badge badge-scheduled\">Scheduled</span>",
        JobStatus.AwaitingContinuation => "<span class=\"badge badge-awaiting\">Awaiting</span>",
        JobStatus.Expired => "<span class=\"badge badge-expired\">Expired</span>",
        _ => $"<span class=\"badge\">{s}</span>",
    };

    internal static string StatusDot(JobStatus status) => status switch
    {
        JobStatus.Processing => "<span class=\"dot dot-processing\"></span>",
        JobStatus.Succeeded => "<span class=\"dot dot-succeeded\"></span>",
        JobStatus.Failed => "<span class=\"dot dot-failed\"></span>",
        JobStatus.Scheduled => "<span class=\"dot dot-scheduled\"></span>",
        JobStatus.Enqueued => "<span class=\"dot dot-enqueued\"></span>",
        JobStatus.AwaitingContinuation => "<span class=\"dot dot-awaiting\"></span>",
        JobStatus.Expired => "<span class=\"dot dot-expired\"></span>",
        _ => "<span class=\"dot dot-default\"></span>",
    };

    internal static string Truncate(string? s, int max)
    {
        if (s is null)
        {
            return string.Empty;
        }

        return s.Length <= max ? s : s[..max] + "…";
    }

    internal static string FormatJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "—";
        }

        try
        {
            // If the JSON is a serialized string containing JSON (e.g. "\"{\\u0022...\}\""), unwrap it first
            var trimmed = json.Trim();
            if (trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"'))
            {
                try
                {
                    var unescaped = System.Text.Json.JsonSerializer.Deserialize<string>(trimmed);
                    if (!string.IsNullOrWhiteSpace(unescaped) && (unescaped.TrimStart().StartsWith('{') || unescaped.TrimStart().StartsWith('[')))
                    {
                        json = unescaped;
                    }
                }
                catch
                {
                    // keep original json if unwrap fails
                }
            }

            var doc = System.Text.Json.JsonDocument.Parse(json);
            var pretty = System.Text.Json.JsonSerializer.Serialize(doc,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            return ColorizeJson(pretty);
        }
        catch
        {
            return System.Web.HttpUtility.HtmlEncode(json);
        }
    }

    internal static string ColorizeJson(string json) =>
        Regex.Replace(
            System.Web.HttpUtility.HtmlEncode(json),
            @"""((?:[^""\\]|\\.)*)""(\s*:)?|(-?\d+\.?\d*(?:[eE][+-]?\d+)?)|(\btrue\b|\bfalse\b|\bnull\b)",
#pragma warning disable MA0023
            ColorizeMatch,
            RegexOptions.None,
#pragma warning restore MA0023
            TimeSpan.FromMilliseconds(100));

    internal static string ColorizeMatch(Match m)
    {
        if (m.Groups[2].Success)
        {
            return $"<span class='jk'>\"{m.Groups[1].Value}\"</span>{m.Groups[2].Value}";
        }

        if (m.Groups[3].Success)
        {
            return $"<span class='jn'>{m.Value}</span>";
        }

        if (m.Groups[4].Success)
        {
            return $"<span class='jb'>{m.Value}</span>";
        }

        return $"<span class='js'>\"{m.Groups[1].Value}\"</span>";
    }

    internal static string FormatCountdown(TimeSpan ts)
    {
        if (ts <= TimeSpan.Zero)
        {
            return "<span style=\"color:var(--warning)\">Due now</span>";
        }

        if (ts.TotalDays >= 1)
        {
            return $"{(int)ts.TotalDays}d {ts.Hours}h";
        }

        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        }

        if (ts.TotalMinutes >= 1)
        {
            return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        }

        return $"{(int)ts.TotalSeconds}s";
    }

    internal static string CountdownFriendly(TimeSpan span)
    {
        if (span.TotalSeconds < 0)
        {
            return "overdue";
        }

        if (span.TotalMinutes < 1)
        {
            return $"in {(int)span.TotalSeconds}s";
        }

        if (span.TotalHours < 1)
        {
            return $"in {(int)span.TotalMinutes}m";
        }

        if (span.TotalDays < 1)
        {
            return $"in {(int)span.TotalHours}h {span.Minutes}m";
        }

        return $"in {(int)span.TotalDays}d {span.Hours}h";
    }

    internal static string RelativeTime(DateTimeOffset? dt, DateTimeOffset now) =>
        dt is null ? "—" :
        (now - dt.Value) switch
        {
            var d when d.TotalSeconds < 60 => $"{(int)d.TotalSeconds}s ago",
            var d when d.TotalMinutes < 60 => $"{(int)d.TotalMinutes}m ago",
            var d when d.TotalHours < 24 => $"{(int)d.TotalHours}h ago",
            var d => $"{(int)d.TotalDays}d ago",
        };

    internal static string FormatSeconds(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        if (span.TotalSeconds < 60)
        {
            return span.TotalSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "s";
        }

        if (span.TotalHours < 1)
        {
            return $"{(int)span.TotalMinutes}m {span.Seconds:D2}s";
        }

        return $"{(int)span.TotalHours}h {span.Minutes:D2}m";
    }

    internal static string? DescribeCron(string cron)
    {
        if (string.IsNullOrWhiteSpace(cron))
        {
            return null;
        }

        var parts = cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 6)
        {
            if (string.Equals(parts[0], "0", StringComparison.Ordinal))
            {
                parts = parts[1..];
            }
            else
            {
                return null;
            }
        }

        if (parts.Length != 5)
        {
            return null;
        }

        var min = parts[0];
        var hour = parts[1];
        var dom = parts[2];
        var mon = parts[3];
        var dow = parts[4];

        static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);

        if (Eq(min, "*") && Eq(hour, "*") && Eq(dom, "*") && Eq(mon, "*") && Eq(dow, "*"))
        {
            return "Every minute";
        }

        if (min.StartsWith("*/", StringComparison.Ordinal) && int.TryParse(min[2..], out var minStep) && Eq(hour, "*") && Eq(dom, "*") && Eq(mon, "*") && Eq(dow, "*"))
        {
            return $"Every {minStep}m";
        }

        if (Eq(min, "0") && Eq(hour, "*") && Eq(dom, "*") && Eq(mon, "*") && Eq(dow, "*"))
        {
            return "Hourly";
        }

        if (Eq(min, "0") && hour.StartsWith("*/", StringComparison.Ordinal) && int.TryParse(hour[2..], out var hourStep) && Eq(dom, "*") && Eq(mon, "*") && Eq(dow, "*"))
        {
            return $"Every {hourStep}h";
        }

        if (int.TryParse(min, out var m) && int.TryParse(hour, out var h) && Eq(dom, "*") && Eq(mon, "*") && Eq(dow, "*"))
        {
            return $"Daily at {h:D2}:{m:D2}";
        }

        if (Eq(min, "0") && Eq(hour, "0") && Eq(dom, "*") && Eq(mon, "*") && Eq(dow, "0"))
        {
            return "Weekly on Sun";
        }

        if (Eq(min, "0") && Eq(hour, "0") && Eq(dom, "1") && Eq(mon, "*") && Eq(dow, "*"))
        {
            return "Monthly (1st)";
        }

        return null;
    }

    internal static (string? TraceId, string? SpanId, bool Sampled) ParseTraceParent(string? traceparent)
    {
        if (string.IsNullOrWhiteSpace(traceparent))
        {
            return (null, null, false);
        }

        var parts = traceparent.Trim().Split('-');
        if (parts.Length >= 4 && string.Equals(parts[0], "00", StringComparison.Ordinal))
        {
            return (parts[1], parts[2], string.Equals(parts[3], "01", StringComparison.Ordinal));
        }

        return (null, null, false);
    }

    internal static string FormatServerId(string? serverId)
    {
        if (string.IsNullOrWhiteSpace(serverId))
        {
            return "—";
        }

        var parts = serverId.Split(':');
        if (parts.Length == 3 && parts[2].Length >= 8)
        {
            return $"{parts[0]}:{parts[1]} #{parts[2][..8]}";
        }

        return serverId;
    }

    internal static string FormatServerIdHtml(string? serverId)
    {
        if (string.IsNullOrWhiteSpace(serverId))
        {
            return "—";
        }

        var parts = serverId.Split(':');
        if (parts.Length == 3 && parts[2].Length >= 8)
        {
            var hostAndPid = System.Web.HttpUtility.HtmlEncode($"{parts[0]}:{parts[1]}");
            var shortGuid = System.Web.HttpUtility.HtmlEncode(parts[2][..8]);
            return $"{hostAndPid} <span style=\"font-size:11px;color:var(--text-tertiary);font-weight:400\">#{shortGuid}</span>";
        }

        return System.Web.HttpUtility.HtmlEncode(serverId);
    }
}
