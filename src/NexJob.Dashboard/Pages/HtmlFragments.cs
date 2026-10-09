using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Web;
using NexJob.Configuration;
using NexJob.Internal;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

/// <summary>Reusable HTML fragment builders for dashboard pages.</summary>
[ExcludeFromCodeCoverage]
internal static class HtmlFragments
{
    /// <summary>Read-only mode warning banner HTML.</summary>
    private const string ReadOnlyBannerHtml =
        """
        <div class="alert alert-warning" style="margin-bottom:16px">
            <svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5" style="vertical-align:middle;margin-right:6px"><path d="M8 1L15 14H1L8 1z"/><line x1="8" y1="6" x2="8" y2="9"/><circle cx="8" cy="12" r=".5" fill="currentColor"/></svg>
            Read-only mode — administrative actions are disabled.
        </div>
        """;

    /// <summary>Renders a status badge with appropriate styling.</summary>
    private static string BadgeClass(string status) => status switch
    {
        "Succeeded" => "badge-success",
        "Processing" => "badge-warning",
        "Failed" => "badge-error",
        "Enqueued" => "badge-info",
        "Awaiting" => "badge-info",
        "Scheduled" => "badge-gray",
        "Expired" => "badge-gray",
        "Cancelled" => "badge-gray",
        _ => "badge-gray",
    };

    /// <summary>Renders a status badge HTML string.</summary>
    public static string StatusBadge(string status)
        => $"<span class=\"badge {BadgeClass(status)}\">{status}</span>";

    /// <summary>Renders a page header with title, subtitle, and optional action buttons.</summary>
    internal static string PageHeader(string title, string subtitle, string? actionsHtml = null) =>
        $"""
        <div class="page-header">
            <div>
                <h2 class="page-title">{HtmlEncode(title)}</h2>
                <p class="page-subtitle">{HtmlEncode(subtitle)}</p>
            </div>
            {(actionsHtml != null ? $"<div class=\"page-actions\">{actionsHtml}</div>" : string.Empty)}
        </div>
        """;

    /// <summary>Empty-state icon: an empty inbox. Valid SVG path data (starts with a moveto command).</summary>
    internal const string EmptyIconInbox = "M22 12h-6l-2 3h-4l-2-3H2M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z";

    /// <summary>Empty-state icon: a server rack.</summary>
    internal const string EmptyIconServer = "M2 4h20v8H2zM2 14h20v8H2zM6 8h.01M6 18h.01";

    /// <summary>Empty-state icon: a check mark inside a circle.</summary>
    internal const string EmptyIconCheck = "M22 11.08V12a10 10 0 1 1-5.93-9.14M22 4 12 14.01l-3-3";

    /// <summary>Empty-state icon: a lightning bolt.</summary>
    internal const string EmptyIconBolt = "M13 2 3 14h9l-1 8 10-12h-9l1-8z";

    /// <summary>Renders a standard empty state with optional SVG icon and message.</summary>
    internal static string EmptyState(string svgPath, string message) =>
        $"""
        <div class="card" style="padding:48px;text-align:center;color:var(--text-tertiary)">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1" style="margin-bottom:16px"><path d="{HtmlEncode(svgPath)}"/></svg>
            <p>{HtmlEncode(message)}</p>
        </div>
        """;

    /// <summary>Renders breadcrumbs navigation.</summary>
    internal static string Breadcrumbs(string pathPrefix, params (string Label, string? Url)[] segments)
    {
        var html = "<div class=\"breadcrumbs\">";
        html += $"<a href=\"{pathPrefix}\">Home</a>";
        foreach (var (label, url) in segments)
        {
            html += "<span class=\"separator\">/</span>";
            if (url != null)
            {
                html += $"<a href=\"{url}\">{HtmlEncode(label)}</a>";
            }
            else
            {
                html += $"<span class=\"current\">{HtmlEncode(label)}</span>";
            }
        }

        html += "</div>";
        return html;
    }

    /// <summary>Renders a job row for the Jobs list page (Single-line).</summary>
    internal static string JobRow(JobRecord job, string pathPrefix, DateTimeOffset now)
    {
        var timeCell = job.Status switch
        {
            JobStatus.Scheduled =>
                job.ScheduledAt.HasValue
                    ? $"<span style=\"color:var(--primary)\">{Helpers.CountdownFriendly(job.ScheduledAt.Value - now)}</span>"
                    : "—",
            JobStatus.Succeeded or JobStatus.Failed =>
                Helpers.RelativeTime(job.CompletedAt, now),
            JobStatus.Processing =>
                $"<span style=\"color:var(--warning)\">running</span>",
            _ => "—",
        };

        var rowClass = job.Status switch
        {
            JobStatus.Failed => "failed",
            JobStatus.Processing => "processing",
            _ => string.Empty,
        };

        var effectiveMax = Helpers.GetEffectiveMaxAttempts(job);
        var attemptInfo = job.Attempts > 1
            ? $" <span style=\"color:var(--text-secondary);font-size:11px\">({job.Attempts}/{effectiveMax})</span>"
            : string.Empty;

        return
            $"<div class=\"job-row {rowClass}\">" +
            $"<input type=\"checkbox\" class=\"job-check\" value=\"{job.Id.Value}\" onclick=\"event.stopPropagation(); nexJobUpdateSelection()\" />" +
            $"<div class=\"job-row-dot\">{Helpers.StatusDot(job.Status)}</div>" +
            $"<a href=\"{pathPrefix}/jobs/{job.Id.Value}\" class=\"job-row-main\" style=\"text-decoration:none;display:flex;align-items:baseline;gap:8px;min-width:0;color:inherit\">" +
                $"<span style=\"overflow:hidden;text-overflow:ellipsis\">{HtmlEncode(Helpers.ShortType(job.JobType))}</span>" +
                $"<span style=\"font-family:monospace;color:var(--text-secondary);font-weight:400;font-size:11px;flex-shrink:0\">#{job.Id.Value.ToString()[..8]}</span>" +
            $"</a>" +
            $"<div onclick=\"event.preventDefault(); event.stopPropagation(); window.location.href='{pathPrefix}/jobs?queue={Uri.EscapeDataString(job.Queue)}'\" style=\"cursor:pointer;color:var(--text-secondary)\">{HtmlEncode(job.Queue)}</div>" +
            $"<div style=\"color:var(--text-secondary)\">Prio {job.Priority}{attemptInfo}</div>" +
            $"<div class=\"job-row-meta\" style=\"text-align:right;color:var(--text-secondary)\">{timeCell}</div>" +
            $"</div>";
    }

    /// <summary>Renders a job row for the Failed Jobs page (with error snippet and inline actions).</summary>
    internal static string JobRowFailed(JobRecord job, string pathPrefix, DateTimeOffset now, DashboardCluster? activeCluster = null)
    {
        var errorSnippet = Helpers.Truncate(job.LastErrorMessage, 90);
        var clusterSuffix = activeCluster is not null ? $"?cluster={Uri.EscapeDataString(activeCluster.Id)}" : string.Empty;
        var isReadOnly = activeCluster?.IsReadOnly == true;

        var requeueForm =
            $"<form method=\"post\" action=\"{pathPrefix}/jobs/{job.Id.Value}/requeue{clusterSuffix}\" style=\"display:inline\">" +
            "<button type=\"submit\" class=\"btn btn-secondary btn-sm\">↺ Requeue</button></form>";
        var deleteForm =
            $"<form method=\"post\" action=\"{pathPrefix}/jobs/{job.Id.Value}/delete{clusterSuffix}\" style=\"display:inline\" " +
            "onclick=\"return confirm('Delete this job?')\">" +
            "<button type=\"submit\" class=\"btn btn-danger btn-sm\">Delete</button></form>";

        var actionsPart = !isReadOnly
            ? $"<div style=\"display:flex;gap:8px;justify-content:flex-end\" onclick=\"event.stopPropagation()\">" +
              requeueForm +
              deleteForm +
              $"</div>"
            : string.Empty;

        var gridCols = !isReadOnly
            ? "24px 32px 1fr 120px 180px"
            : "32px 1fr 120px";

        var checkCol = !isReadOnly
            ? $"<input type=\"checkbox\" class=\"job-check\" value=\"{job.Id.Value}\" onclick=\"event.stopPropagation(); nexJobUpdateSelection()\" />"
            : string.Empty;

        return
            $"<div class=\"job-row\" style=\"grid-template-columns: {gridCols}; padding:16px 24px\">" +
            checkCol +
            $"<div class=\"job-row-dot\">{Helpers.StatusDot(JobStatus.Failed)}</div>" +
            $"<a href=\"{pathPrefix}/jobs/{job.Id.Value}{clusterSuffix}\" style=\"text-decoration:none;color:inherit;min-width:0\">" +
                $"<div class=\"job-row-main\">" +
                    $"<div class=\"job-row-title\">{HtmlEncode(Helpers.ShortType(job.JobType))} <span style=\"font-family:monospace;font-size:11px;color:var(--text-secondary)\">#{job.Id.Value.ToString()[..8]}</span></div>" +
                    $"<div style=\"font-size:12px;color:var(--error);margin-top:2px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis\">{HtmlEncode(errorSnippet)}</div>" +
                $"</div>" +
            $"</a>" +
            $"<div class=\"job-row-meta\" style=\"font-size:12px;color:var(--text-secondary)\">" +
                $"{Helpers.RelativeTime(job.CompletedAt, now)}" +
            $"</div>" +
            actionsPart +
            $"</div>";
    }

    /// <summary>Renders status filter pills for the Failed page (Failed vs Expired).</summary>
    internal static string FailedStatusPills(string currentStatus, string baseUrl)
    {
        var pills = string.Join(string.Empty, new[]
        {
            ("Failed", "Failed"),
            ("Expired", "Expired"),
        }.Select(o =>
        {
            var active = string.Equals(currentStatus, o.Item1, StringComparison.Ordinal) ? " active" : string.Empty;
            var qs = $"?status={Uri.EscapeDataString(o.Item1)}";
            return $"<a href=\"{baseUrl}{qs}\" class=\"nav-item{active}\" style=\"padding:6px 12px;font-size:12px\">{o.Item2}</a>";
        }));
        return $"<div style=\"display:flex;gap:4px;margin-bottom:16px\">{pills}</div>";
    }

    /// <summary>Renders the filter bar for the Jobs page with search, tag, queue, status, and period filters.</summary>
    internal static string FilterBar(string pathPrefix, string currentStatus, string? search, string? tag, string? queue = null, IReadOnlyList<QueueMetrics>? queues = null, string? period = null)
    {
        var searchVal = HttpUtility.HtmlAttributeEncode(search ?? string.Empty);
        var tagVal = HttpUtility.HtmlAttributeEncode(tag ?? string.Empty);
        var queueVal = queue ?? string.Empty;
        var periodVal = period ?? string.Empty;

        var statusOptions = string.Join(string.Empty, new[]
        {
            (string.Empty, "All Statuses"),
            ("Enqueued", "Enqueued"),
            ("Processing", "Processing"),
            ("Succeeded", "Succeeded"),
            ("Failed", "Failed"),
            ("Scheduled", "Scheduled"),
            ("Awaiting", "Awaiting"),
            ("Expired", "Expired"),
            ("Cancelled", "Cancelled"),
        }.Select(o =>
        {
            var selected = string.Equals(currentStatus, o.Item1, StringComparison.Ordinal) ? " selected" : string.Empty;
            return $"<option value=\"{HttpUtility.HtmlAttributeEncode(o.Item1)}\"{selected}>{o.Item2}</option>";
        }));

        var queueOptions = "<option value=\"\">All Queues</option>";
        if (queues != null)
        {
            queueOptions += string.Join(string.Empty, queues.Select(q =>
            {
                var selected = string.Equals(queueVal, q.Queue, StringComparison.Ordinal) ? " selected" : string.Empty;
                return $"<option value=\"{HttpUtility.HtmlAttributeEncode(q.Queue)}\"{selected}>{q.Queue} ({q.Enqueued + q.Processing})</option>";
            }));
        }

        var periodOptions = string.Join(string.Empty, new[]
        {
            (string.Empty, "All Time"),
            ("1h", "Last 1 hour"),
            ("6h", "Last 6 hours"),
            ("24h", "Last 24 hours"),
            ("7d", "Last 7 days"),
        }.Select(o =>
        {
            var selected = string.Equals(periodVal, o.Item1, StringComparison.OrdinalIgnoreCase) ? " selected" : string.Empty;
            return $"<option value=\"{HttpUtility.HtmlAttributeEncode(o.Item1)}\"{selected}>{o.Item2}</option>";
        }));

        var chips = new List<string>();
        if (!string.IsNullOrEmpty(search))
        {
            var removeUrl = $"{pathPrefix}/jobs?status={Uri.EscapeDataString(currentStatus)}&tag={Uri.EscapeDataString(tag ?? string.Empty)}&queue={Uri.EscapeDataString(queueVal)}&period={Uri.EscapeDataString(periodVal)}";
            chips.Add($"<span class=\"badge badge-gray\" style=\"display:inline-flex;align-items:center;gap:6px;padding:4px 8px\">Search: <strong>{searchVal}</strong><a href=\"{removeUrl}\" style=\"color:inherit;text-decoration:none;font-weight:700\">✕</a></span>");
        }

        if (!string.IsNullOrEmpty(currentStatus))
        {
            var removeUrl = $"{pathPrefix}/jobs?search={Uri.EscapeDataString(search ?? string.Empty)}&tag={Uri.EscapeDataString(tag ?? string.Empty)}&queue={Uri.EscapeDataString(queueVal)}&period={Uri.EscapeDataString(periodVal)}";
            chips.Add($"<span class=\"badge badge-gray\" style=\"display:inline-flex;align-items:center;gap:6px;padding:4px 8px\">Status: <strong>{HttpUtility.HtmlEncode(currentStatus)}</strong><a href=\"{removeUrl}\" style=\"color:inherit;text-decoration:none;font-weight:700\">✕</a></span>");
        }

        if (!string.IsNullOrEmpty(queue))
        {
            var removeUrl = $"{pathPrefix}/jobs?status={Uri.EscapeDataString(currentStatus)}&search={Uri.EscapeDataString(search ?? string.Empty)}&tag={Uri.EscapeDataString(tag ?? string.Empty)}&period={Uri.EscapeDataString(periodVal)}";
            chips.Add($"<span class=\"badge badge-gray\" style=\"display:inline-flex;align-items:center;gap:6px;padding:4px 8px\">Queue: <strong>{HttpUtility.HtmlEncode(queue)}</strong><a href=\"{removeUrl}\" style=\"color:inherit;text-decoration:none;font-weight:700\">✕</a></span>");
        }

        if (!string.IsNullOrEmpty(period))
        {
            var removeUrl = $"{pathPrefix}/jobs?status={Uri.EscapeDataString(currentStatus)}&search={Uri.EscapeDataString(search ?? string.Empty)}&tag={Uri.EscapeDataString(tag ?? string.Empty)}&queue={Uri.EscapeDataString(queueVal)}";
            chips.Add($"<span class=\"badge badge-gray\" style=\"display:inline-flex;align-items:center;gap:6px;padding:4px 8px\">Period: <strong>{HttpUtility.HtmlEncode(period)}</strong><a href=\"{removeUrl}\" style=\"color:inherit;text-decoration:none;font-weight:700\">✕</a></span>");
        }

        if (!string.IsNullOrEmpty(tag))
        {
            var removeUrl = $"{pathPrefix}/jobs?status={Uri.EscapeDataString(currentStatus)}&search={Uri.EscapeDataString(search ?? string.Empty)}&queue={Uri.EscapeDataString(queueVal)}&period={Uri.EscapeDataString(periodVal)}";
            chips.Add($"<span class=\"badge badge-gray\" style=\"display:inline-flex;align-items:center;gap:6px;padding:4px 8px\">Tag: <strong>{tagVal}</strong><a href=\"{removeUrl}\" style=\"color:inherit;text-decoration:none;font-weight:700\">✕</a></span>");
        }

        var activeChipsHtml = string.Empty;
        if (chips.Count > 0)
        {
            activeChipsHtml =
                $"<div class=\"active-filters\" style=\"display:flex;align-items:center;gap:8px;margin-bottom:14px;flex-wrap:wrap;font-size:12px\">" +
                $"<span style=\"color:var(--text-tertiary);font-weight:600;font-size:11px;text-transform:uppercase\">Active Filters:</span>" +
                string.Join(string.Empty, chips) +
                $"<a href=\"{pathPrefix}/jobs\" class=\"btn btn-ghost btn-sm\" style=\"font-size:11px;padding:2px 6px;color:var(--text-secondary)\">Clear all</a>" +
                $"</div>";
        }

        return
            $"<div class=\"filters\">" +
            $"<form method=\"get\" action=\"{pathPrefix}/jobs\" style=\"display:flex;gap:8px;margin-bottom:12px;flex-wrap:wrap;width:100%\">" +
            $"<div style=\"display:flex;gap:4px;flex:1;min-width:200px\">" +
            $"<input type=\"text\" name=\"search\" placeholder=\"Search type or ID…\" value=\"{searchVal}\" style=\"flex:1\" />" +
            $"</div>" +
            $"<select name=\"status\" style=\"width:140px\">{statusOptions}</select>" +
            $"<select name=\"queue\" style=\"width:140px\">{queueOptions}</select>" +
            $"<select name=\"period\" style=\"width:130px\">{periodOptions}</select>" +
            $"<input type=\"text\" name=\"tag\" placeholder=\"Tag…\" value=\"{tagVal}\" style=\"width:110px\" />" +
            $"<div style=\"display:flex;gap:4px\">" +
            $"<button type=\"submit\" class=\"btn btn-primary\">Filter</button>" +
            (searchVal.Length > 0 || tagVal.Length > 0 || queueVal.Length > 0 || currentStatus.Length > 0 || periodVal.Length > 0
                ? $"<a href=\"{pathPrefix}/jobs\" class=\"btn btn-secondary\">Clear</a>"
                : string.Empty) +
            $"</div>" +
            $"</form>" +
            activeChipsHtml +
            $"</div>";
    }

    /// <summary>Renders pagination controls for paginated results.</summary>
    internal static string Pagination(PagedResult<JobRecord> result, string baseUrl)
    {
        if (result.TotalPages <= 1)
        {
            return string.Empty;
        }

        var prev = result.Page > 1
            ? $"<a href=\"{baseUrl}&page={result.Page - 1}\" class=\"btn btn-secondary btn-sm\">← Prev</a>"
            : "<span class=\"btn btn-secondary btn-sm\" style=\"opacity:.3;cursor:default\">← Prev</span>";

        var next = result.Page < result.TotalPages
            ? $"<a href=\"{baseUrl}&page={result.Page + 1}\" class=\"btn btn-secondary btn-sm\">Next →</a>"
            : "<span class=\"btn btn-secondary btn-sm\" style=\"opacity:.3;cursor:default\">Next →</span>";

        return $"<div class=\"pagination\" style=\"display:flex;align-items:center;gap:12px;margin-top:16px\">{prev}{next}<span class=\"page-info\" style=\"font-size:12px;color:var(--text-tertiary)\">Page {result.Page} of {result.TotalPages} ({result.TotalCount} jobs)</span></div>";
    }

    /// <summary>Renders a progress bar section with percentage and optional message.</summary>
    internal static string ProgressBar(int? percentage, string? message = null)
    {
        if (!percentage.HasValue)
        {
            return string.Empty;
        }

        var pct = percentage.Value;
        var msgHtml = message is not null ? HtmlEncode(message) : string.Empty;

        return
            $"<div class=\"progress-wrap\" style=\"margin-bottom:24px\">" +
            $"<div class=\"progress-bar-track\" style=\"height:8px;background:var(--bg-tertiary);border-radius:4px;overflow:hidden;margin-bottom:8px\">" +
            $"<div id=\"progress-bar-fill\" class=\"progress-bar-fill\" style=\"width:{pct}%;height:100%;background:var(--primary);transition:width 0.3s ease\"></div>" +
            $"</div>" +
            $"<div class=\"progress-info\" style=\"display:flex;justify-content:space-between;font-size:12px\">" +
            $"<span id=\"progress-msg\" style=\"color:var(--text-secondary)\">{msgHtml}</span>" +
            $"<span id=\"progress-pct\" style=\"font-weight:700;color:var(--primary)\">{pct}%</span>" +
            $"</div>" +
            $"</div>";
    }

    /// <summary>Renders the error section with message and optional stack trace.</summary>
    internal static string ErrorSection(string? errorMessage, string? stackTrace = null)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return string.Empty;
        }

        var stackTraceHtml = !string.IsNullOrWhiteSpace(stackTrace)
            ? $"<div class=\"terminal-window\" style=\"margin-top:16px\">" +
              $"<div class=\"terminal-header\"><div class=\"terminal-dots\"><span></span><span></span><span></span></div><span class=\"terminal-title\">stack-trace.log</span></div>" +
              $"<div class=\"terminal-body\"><pre style=\"margin:0;font-size:12px;color:#e2e8f0;overflow-x:auto;font-family:monospace;white-space:pre-wrap\">{HtmlEncode(stackTrace)}</pre></div>" +
              $"</div>"
            : string.Empty;

        return
            $"<div style=\"margin-bottom:24px\">" +
            $"<h3 style=\"margin-bottom:8px;color:var(--error);font-size:14px;font-weight:600\">Last Error</h3>" +
            $"<div class=\"terminal-window\">" +
            $"<div class=\"terminal-header\"><div class=\"terminal-dots\"><span></span><span></span><span></span></div><span class=\"terminal-title\">error.log</span></div>" +
            $"<div class=\"terminal-body\"><pre style=\"margin:0;font-size:12px;color:var(--error);overflow-x:auto;font-family:monospace;white-space:pre-wrap\">{HtmlEncode(errorMessage)}</pre></div>" +
            $"</div>" +
            stackTraceHtml +
            $"</div>";
    }

    /// <summary>Renders a metric card matching the stat-grid style.</summary>
    internal static string MetricCard(string elementId, string label, long value, string iconClass, string svgHtml, string sublabel, string? url = null)
    {
        var content =
            $"<div class=\"stat-card\">" +
            $"<div class=\"stat-icon {iconClass}\">{svgHtml}</div>" +
            $"<div class=\"stat-content\">" +
            $"<div id=\"{elementId}\" class=\"stat-value\">{value}</div>" +
            $"<div class=\"stat-label\">{HtmlEncode(label)}</div>" +
            $"<div class=\"stat-sublabel\">{HtmlEncode(sublabel)}</div>" +
            $"</div>" +
            $"</div>";

        if (string.IsNullOrEmpty(url))
        {
            return content;
        }

        return $"<a href=\"{url}\" style=\"text-decoration:none;color:inherit\">{content}</a>";
    }

    /// <summary>Renders a compact circular SVG gauge for volatile metrics (CPU, RAM).</summary>
    internal static string CircularGauge(int percent, string centerText, string label, string subtext, string color, int size = 36)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        var dashOffset = 100 - clamped;
        return
            $"<div class=\"mini-gauge-wrap\" title=\"{HtmlAttributeEncode(label)}: {clamped}% ({HtmlAttributeEncode(subtext)})\">" +
            $"<svg width=\"{size}\" height=\"{size}\" viewBox=\"0 0 36 36\" class=\"mini-gauge-svg\">" +
            $"<path class=\"gauge-bg\" d=\"M18 2.0845 a 15.9155 15.9155 0 0 1 0 31.831 a 15.9155 15.9155 0 0 1 0 -31.831\" fill=\"none\" stroke=\"var(--bg-tertiary)\" stroke-width=\"3.5\" />" +
            $"<path class=\"gauge-bar\" stroke-dasharray=\"100, 100\" stroke-dashoffset=\"{dashOffset}\" d=\"M18 2.0845 a 15.9155 15.9155 0 0 1 0 31.831 a 15.9155 15.9155 0 0 1 0 -31.831\" fill=\"none\" stroke=\"{color}\" stroke-width=\"3.5\" stroke-linecap=\"round\" />" +
            $"<text x=\"18\" y=\"20.5\" class=\"gauge-val-text\" text-anchor=\"middle\" font-size=\"8.5\" font-weight=\"700\" fill=\"var(--text-primary)\">{HtmlEncode(centerText)}</text>" +
            $"</svg>" +
            $"<div class=\"mini-gauge-info\">" +
            $"<span class=\"mini-gauge-title\">{HtmlEncode(label)}</span>" +
            $"<span class=\"mini-gauge-sub\">{HtmlEncode(subtext)}</span>" +
            $"</div>" +
            $"</div>";
    }

    internal static string GetCpuColor(int percent)
    {
        if (percent >= 85)
        {
            return "var(--error)";
        }

        if (percent >= 60)
        {
            return "var(--warning)";
        }

        return "var(--primary)";
    }

    internal static string GetRamColor(int percent)
    {
        if (percent >= 90)
        {
            return "var(--error)";
        }

        if (percent >= 70)
        {
            return "var(--warning)";
        }

        return "var(--success)";
    }

    /// <summary>Renders a compact queue row for high-density monitoring with control actions.</summary>
    internal static string QueueCard(
        QueueMetrics queue,
        string pathPrefix,
        bool isPaused = false,
        DashboardCluster? activeCluster = null,
        bool hasActiveWorkers = true,
        QueueCircuitStatus? circuitStatus = null,
        string? orphanHint = null)
    {
        var total = queue.Enqueued + queue.Processing;
        var utilPct = total > 0 ? (int)(queue.Processing * 100.0 / total) : 0;
        var clusterSuffix = activeCluster is not null ? $"?cluster={Uri.EscapeDataString(activeCluster.Id)}" : string.Empty;
        var isReadOnly = activeCluster?.IsReadOnly == true;

        var utilColor = utilPct switch
        {
            > 80 => "var(--error)",
            > 50 => "var(--warning)",
            _ => "var(--success)",
        };

        var pauseForm = string.Empty;
        if (!isReadOnly)
        {
            var resetForm = string.Empty;
            if (circuitStatus is not null && circuitStatus.State != QueueCircuitState.Closed)
            {
                resetForm = $"<form method=\"post\" action=\"{pathPrefix}/queues/{Uri.EscapeDataString(queue.Queue)}/reset-circuit{clusterSuffix}\" style=\"display:inline\" onclick=\"return confirm('Reset circuit breaker for queue {HtmlEncode(queue.Queue)}? This will immediately resume traffic to downstream API.')\">" +
                    $"<button type=\"submit\" class=\"btn btn-secondary btn-sm\" title=\"Reset Circuit Breaker\">⚡ Reset Circuit</button></form>";
            }

            pauseForm = isPaused
                ? $"<form method=\"post\" action=\"{pathPrefix}/queues/{Uri.EscapeDataString(queue.Queue)}/resume{clusterSuffix}\" style=\"display:inline\">" +
                  $"<button type=\"submit\" class=\"btn btn-primary btn-sm\" title=\"Resume Queue\">▶ Resume</button></form>"
                : $"<form method=\"post\" action=\"{pathPrefix}/queues/{Uri.EscapeDataString(queue.Queue)}/pause{clusterSuffix}\" style=\"display:inline\" onclick=\"return confirm('Pause queue {HtmlEncode(queue.Queue)}?')\">" +
                  $"<button type=\"submit\" class=\"btn btn-secondary btn-sm\" title=\"Pause Queue\">⏸ Pause</button></form>";

            if (!string.IsNullOrEmpty(resetForm))
            {
                pauseForm = resetForm + pauseForm;
            }
        }

        var statusBadge = isPaused
            ? " <span class=\"badge badge-warning\" style=\"font-size:10px;margin-left:6px\">PAUSED</span>"
            : string.Empty;

        var circuitBadge = string.Empty;
        var failedLink = string.Empty;
        if (circuitStatus is not null)
        {
            var failedUrl = $"{pathPrefix}/failed?queue={Uri.EscapeDataString(queue.Queue)}{(activeCluster is not null ? $"&cluster={Uri.EscapeDataString(activeCluster.Id)}" : string.Empty)}";
            if (circuitStatus.State == QueueCircuitState.Open)
            {
                var remainingSec = circuitStatus.RemainingCooldown.HasValue
                    ? $" ({(int)circuitStatus.RemainingCooldown.Value.TotalSeconds}s)"
                    : string.Empty;
                circuitBadge = $" <a href=\"{failedUrl}\" style=\"text-decoration:none\"><span class=\"badge badge-danger\" style=\"font-size:10px;margin-left:6px;cursor:pointer\" title=\"Circuit is OPEN due to downstream failures. Click to view failed errors.\">⚡ CIRCUIT OPEN{remainingSec}</span></a>";
                failedLink = $"<a href=\"{failedUrl}\" class=\"btn btn-secondary btn-sm\" style=\"color:var(--error);border-color:var(--error)\">View Errors</a>";
            }
            else if (circuitStatus.State == QueueCircuitState.HalfOpen)
            {
                circuitBadge = $" <a href=\"{failedUrl}\" style=\"text-decoration:none\"><span class=\"badge badge-warning\" style=\"font-size:10px;margin-left:6px;cursor:pointer\" title=\"Canary probe in flight to test downstream service health. Click to view failed errors.\">🟡 CANARY TESTING</span></a>";
                failedLink = $"<a href=\"{failedUrl}\" class=\"btn btn-secondary btn-sm\" style=\"color:var(--warning);border-color:var(--warning)\">View Errors</a>";
            }
            else if (circuitStatus.State == QueueCircuitState.Recovering)
            {
                circuitBadge = $" <a href=\"{failedUrl}\" style=\"text-decoration:none\"><span class=\"badge badge-info\" style=\"font-size:10px;margin-left:6px;cursor:pointer\" title=\"Ramp-up mode active to prevent thundering herd (max concurrency: {circuitStatus.AllowedConcurrency}). Click to view failed errors.\">🟢 RECOVERING</span></a>";
                failedLink = $"<a href=\"{failedUrl}\" class=\"btn btn-secondary btn-sm\" style=\"color:var(--info);border-color:var(--info)\">View Errors</a>";
            }
        }

        const string OrphanTooltip = "No active worker nodes are listening to this queue. Jobs will remain enqueued until a worker configured for this queue is online.";
        var orphanTitle = HttpUtility.HtmlEncode(orphanHint is null ? OrphanTooltip : $"{OrphanTooltip} {orphanHint}");
        var orphanWarning = !hasActiveWorkers && queue.Enqueued > 0
            ? $" <a href=\"{pathPrefix}/servers{clusterSuffix}\" style=\"text-decoration:none\"><span class=\"badge badge-warning\" style=\"font-size:10px;margin-left:6px\" title=\"{orphanTitle}\">⚠️ NO WORKERS</span></a>"
            : string.Empty;

        var orphanSubtitle = !hasActiveWorkers && queue.Enqueued > 0
            ? "<div style=\"font-size:11px;color:var(--warning);margin-top:2px;font-weight:400\">⚠️ No active workers listening</div>"
            : string.Empty;

        return
            $"<div style=\"padding:16px 24px;display:flex;align-items:center;gap:24px;border-bottom:1px solid var(--border)\">" +
            $"<div style=\"width:220px;font-weight:600;font-size:15px;color:var(--text-primary);overflow:hidden;text-overflow:ellipsis;white-space:nowrap\">" +
            $"{HtmlEncode(queue.Queue)}{statusBadge}{circuitBadge}{orphanWarning}{orphanSubtitle}</div>" +
            $"<div style=\"flex:1;display:flex;gap:40px;align-items:center\">" +
                $"<div style=\"width:150px;display:flex;align-items:baseline;gap:8px\"><div style=\"font-size:10px;color:var(--text-tertiary);font-weight:700\">ENQUEUED</div><div style=\"font-weight:700;color:var(--info);font-size:18px\">{queue.Enqueued}</div></div>" +
                $"<div style=\"width:150px;display:flex;align-items:baseline;gap:8px\"><div style=\"font-size:10px;color:var(--text-tertiary);font-weight:700\">PROCESSING</div><div style=\"font-weight:700;color:var(--warning);font-size:18px\">{queue.Processing}</div></div>" +
                $"<div style=\"flex:1\">" +
                    $"<div style=\"display:flex;justify-content:space-between;margin-bottom:4px\"><span style=\"font-size:10px;color:var(--text-tertiary);font-weight:700\">UTILIZATION</span><span style=\"font-size:10px;font-weight:700;color:{utilColor}\">{utilPct}%</span></div>" +
                    $"<div style=\"height:6px;background:var(--bg-tertiary);border-radius:3px;overflow:hidden\"><div style=\"width:{utilPct}%;background:{utilColor};height:100%;transition:width 0.3s ease\"></div></div>" +
                $"</div>" +
            $"</div>" +
            $"<div style=\"display:flex;gap:8px;align-items:center;justify-content:flex-end\">" +
                failedLink +
                pauseForm +
                $"<a href=\"{pathPrefix}/jobs?queue={Uri.EscapeDataString(queue.Queue)}{(activeCluster is not null ? $"&cluster={Uri.EscapeDataString(activeCluster.Id)}" : string.Empty)}\" class=\"btn btn-secondary btn-sm\">View Jobs</a>" +
            $"</div>" +
            $"</div>";
    }

    /// <summary>Renders a high-density table row for a recurring job.</summary>
    internal static string RecurringRow(RecurringJobRecord job, string pathPrefix, DateTimeOffset now, DashboardCluster? activeCluster = null, IReadOnlySet<string>? activeWorkerQueues = null)
    {
        var effectiveCron = job.CronOverride ?? job.Cron;
        var encodedIdUrl = Uri.EscapeDataString(job.RecurringJobId);
        var clusterSuffix = activeCluster is not null ? $"?cluster={Uri.EscapeDataString(activeCluster.Id)}" : string.Empty;
        var isReadOnly = activeCluster?.IsReadOnly == true;

        var rowStyle = !job.Enabled ? "style=\"opacity:0.75;background:var(--bg-secondary)\"" : string.Empty;

        string statusBadge;
        string dotClass;
        if (job.DeletedByUser)
        {
            statusBadge = "<span class=\"badge badge-error\" style=\"font-size:11px;padding:2px 8px\">Deleted</span>";
            dotClass = "dot-failed";
        }
        else if (job.Enabled)
        {
            statusBadge = "<span class=\"badge badge-success\" style=\"font-size:11px;padding:2px 8px\">Active</span>";
            dotClass = "dot-succeeded";
        }
        else
        {
            statusBadge = "<span class=\"badge badge-warning\" style=\"font-size:11px;padding:2px 8px\">Paused</span>";
            dotClass = "dot-processing";
        }

        var statusDot = $"<span class=\"dot {dotClass}\" style=\"width:8px;height:8px\"></span>";

        var cronDesc = Helpers.DescribeCron(effectiveCron);
        var cronOverrideTag = job.CronOverride is not null
            ? "<span style=\"font-size:10px;color:var(--warning);margin-left:4px\" title=\"Schedule overridden by operator\">[custom]</span>"
            : string.Empty;
        var cronDescHtml = cronDesc is not null
            ? $"<div style=\"font-size:11px;color:var(--text-tertiary);margin-top:2px\">{HtmlEncode(cronDesc)}</div>"
            : string.Empty;

        string nextHtml = "<span style=\"color:var(--text-tertiary)\">—</span>";
        if (!job.DeletedByUser && job.Enabled && job.NextExecution.HasValue)
        {
            if (job.NextExecution.Value <= now)
            {
                nextHtml = "<span class=\"badge badge-warning\" style=\"font-size:10px;animation:pulse 2s infinite\">DUE NOW</span>";
            }
            else
            {
                var exactTime = job.NextExecution.Value.ToString("HH:mm:ss 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
                nextHtml =
                    $"<div style=\"display:flex;flex-direction:column;gap:1px\">" +
                    $"<span style=\"font-size:12px;font-weight:500;color:var(--text-primary)\">in {Helpers.FormatCountdown(job.NextExecution.Value - now)}</span>" +
                    $"<span style=\"font-size:10px;color:var(--text-tertiary)\">{exactTime}</span>" +
                    $"</div>";
            }
        }

        // Last run
        string lastRunHtml = "<span style=\"color:var(--text-tertiary);font-size:12px\">Never</span>";
        if (job.LastExecutedAt.HasValue)
        {
            var isSuccess = job.LastExecutionStatus == JobStatus.Succeeded;
            var lastDotClass = isSuccess ? "dot-succeeded" : "dot-failed";
            var statusText = job.LastExecutionStatus?.ToString() ?? (isSuccess ? "Succeeded" : "Failed");
            var relative = Helpers.RelativeTime(job.LastExecutedAt, now);

            lastRunHtml =
                $"<div style=\"display:flex;flex-direction:column;gap:1px\">" +
                $"<div style=\"display:flex;align-items:center;gap:6px\">" +
                $"<span class=\"dot {lastDotClass}\" style=\"width:6px;height:6px\"></span>" +
                $"<span style=\"font-size:12px;font-weight:500;color:var(--text-primary)\">{statusText}</span>" +
                $"</div>" +
                $"<span style=\"font-size:10px;color:var(--text-tertiary)\">{relative}</span>" +
                $"</div>";
        }

        var boltIcon = "<svg width=\"14\" height=\"14\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><path d=\"M13 2L3 14h9l-1 8 10-12h-9l1-8z\"/></svg>";
        var pauseIcon = "<svg width=\"14\" height=\"14\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><rect x=\"6\" y=\"4\" width=\"4\" height=\"16\"/><rect x=\"14\" y=\"4\" width=\"4\" height=\"16\"/></svg>";
        var playIcon = "<svg width=\"14\" height=\"14\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><polygon points=\"5 3 19 12 5 21 5 3\"/></svg>";

        string actionsHtml;
        if (isReadOnly)
        {
            actionsHtml = string.Empty;
        }
        else if (job.DeletedByUser)
        {
            actionsHtml = $"<form method=\"post\" action=\"{pathPrefix}/recurring/{encodedIdUrl}/restore{clusterSuffix}\" style=\"display:inline\" onclick=\"event.stopPropagation()\"><button type=\"submit\" class=\"btn btn-secondary btn-sm\">Restore</button></form>";
        }
        else
        {
            var pauseResume = job.Enabled
                ? $"<form method=\"post\" action=\"{pathPrefix}/recurring/{encodedIdUrl}/pause{clusterSuffix}\" style=\"display:inline\" onclick=\"event.stopPropagation()\"><button type=\"submit\" class=\"btn-icon-sm\" title=\"Pause Schedule\" style=\"color:var(--warning)\">{pauseIcon}</button></form>"
                : $"<form method=\"post\" action=\"{pathPrefix}/recurring/{encodedIdUrl}/resume{clusterSuffix}\" style=\"display:inline\" onclick=\"event.stopPropagation()\"><button type=\"submit\" class=\"btn-icon-sm\" title=\"Resume Schedule\" style=\"color:var(--success)\">{playIcon}</button></form>";

            var queueOrphanWarning = activeWorkerQueues is { Count: > 0 } && !activeWorkerQueues.Contains(job.Queue, StringComparer.OrdinalIgnoreCase)
                ? $"\\n\\n⚠ Warning: Queue '{job.Queue}' has no active workers. The job will remain queued until a worker starts."
                : string.Empty;
            var triggerConfirmJs = System.Web.HttpUtility.JavaScriptStringEncode($"Trigger '{job.RecurringJobId}' now?{queueOrphanWarning}", addDoubleQuotes: true);

            actionsHtml =
                $"<form method=\"post\" action=\"{pathPrefix}/recurring/{encodedIdUrl}/trigger{clusterSuffix}\" style=\"display:inline\" onclick=\"event.stopPropagation()\">" +
                $"<button type=\"submit\" class=\"btn-icon-sm\" title=\"Trigger Now\" style=\"color:var(--primary)\" onclick=\"return confirm({triggerConfirmJs})\">{boltIcon}</button></form> " +
                pauseResume;
        }

        var actionsTd = !isReadOnly
            ? $"<td style=\"padding:12px 20px;text-align:right\"><div style=\"display:flex;gap:6px;justify-content:flex-end\">{actionsHtml}</div></td>"
            : string.Empty;

        return
            $"<tr class=\"table-recurring\" {rowStyle} onclick=\"window.location.href='{pathPrefix}/recurring/{encodedIdUrl}{clusterSuffix}'\" style=\"cursor:pointer\">" +
            $"<td style=\"padding:12px 20px\"><div style=\"display:flex;align-items:center;gap:8px\">{statusDot}{statusBadge}</div></td>" +
            $"<td style=\"padding:12px 20px\"><div style=\"display:flex;flex-direction:column;gap:2px\">" +
            $"<div style=\"font-weight:600;font-size:13px;color:var(--text-primary)\">{HtmlEncode(job.RecurringJobId)}</div>" +
            $"<div style=\"font-size:11px;color:var(--text-tertiary);font-family:monospace\">{HtmlEncode(Helpers.ShortType(job.JobType))}</div>" +
            $"</div></td>" +
            $"<td style=\"padding:12px 20px\"><div style=\"display:flex;flex-direction:column;gap:2px\">" +
            $"<div><code style=\"background:var(--bg-tertiary);color:var(--text-secondary);padding:3px 7px;border-radius:4px;font-size:11px;border:1px solid var(--border)\">{HtmlEncode(effectiveCron)}</code>{cronOverrideTag}</div>" +
            $"{cronDescHtml}" +
            $"</div></td>" +
            $"<td style=\"padding:12px 20px\"><span style=\"background:var(--bg-tertiary);color:var(--text-secondary);padding:3px 8px;border-radius:12px;font-size:11px;border:1px solid var(--border);font-family:monospace\">{HtmlEncode(job.Queue)}</span></td>" +
            $"<td style=\"padding:12px 20px\">{lastRunHtml}</td>" +
            $"<td style=\"padding:12px 20px\">{nextHtml}</td>" +
            actionsTd +
            $"</tr>";
    }

    /// <summary>Returns the read-only mode warning banner HTML.</summary>
    internal static string ReadOnlyBanner() => ReadOnlyBannerHtml;

    /// <summary>Renders a visual execution flow timeline and state transition pipeline.</summary>
    internal static string ExecutionTimeline(JobRecord job, DateTimeOffset now)
    {
        var sb = new System.Text.StringBuilder();

        // 1. Calculate timing metrics
        var enqueuedAt = job.ScheduledAt ?? job.CreatedAt;
        string waitTimeStr = "—";
        if (job.ProcessingStartedAt.HasValue)
        {
            var waitSpan = job.ProcessingStartedAt.Value - enqueuedAt;
            if (waitSpan < TimeSpan.Zero)
            {
                waitSpan = TimeSpan.Zero;
            }

            waitTimeStr = $"{Helpers.FormatSeconds(waitSpan)} wait";
        }
        else if (job.Status == JobStatus.Enqueued)
        {
            var waitSpan = now - enqueuedAt;
            if (waitSpan < TimeSpan.Zero)
            {
                waitSpan = TimeSpan.Zero;
            }

            waitTimeStr = $"{Helpers.FormatSeconds(waitSpan)} wait";
        }

        string durationStr = "—";
        if (job.ProcessingStartedAt.HasValue)
        {
            if (job.CompletedAt.HasValue)
            {
                var durSpan = job.CompletedAt.Value - job.ProcessingStartedAt.Value;
                if (durSpan < TimeSpan.Zero)
                {
                    durSpan = TimeSpan.Zero;
                }

                durationStr = $"{Helpers.FormatSeconds(durSpan)} run";
            }
            else
            {
                var durSpan = now - job.ProcessingStartedAt.Value;
                if (durSpan < TimeSpan.Zero)
                {
                    durSpan = TimeSpan.Zero;
                }

                durationStr = $"running {Helpers.FormatSeconds(durSpan)}";
            }
        }

        var totalAge = Helpers.RelativeTime(job.CreatedAt, now);
        var effectiveMax = Helpers.GetEffectiveMaxAttempts(job);

        // 2. Card Header
        sb.Append("<div class=\"lifecycle-card\">");
        sb.Append("<div class=\"lifecycle-header\">");
        sb.Append("<div>");
        sb.Append("<h3 style=\"font-size:15px;font-weight:700;text-transform:uppercase;letter-spacing:0.5px;color:var(--text-secondary);margin:0;display:flex;align-items:center;gap:8px\">");
        sb.Append("<svg width=\"18\" height=\"18\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"var(--primary)\" stroke-width=\"2.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><polyline points=\"22 12 18 12 15 21 9 3 6 12 2 12\"></polyline></svg>");
        sb.Append("<span>Lifecycle Execution Flow</span>");
        sb.Append("</h3>");
        sb.Append("<p style=\"font-size:12px;color:var(--text-tertiary);margin:4px 0 0 0\">Visual state transitions, queue wait latency, and worker execution metrics</p>");
        sb.Append("</div>");

        sb.Append("<div style=\"display:flex;gap:8px;align-items:center;flex-wrap:wrap\">");
        sb.Append($"<span class=\"badge\" style=\"background:var(--bg-tertiary);color:var(--text-secondary);border:1px solid var(--border)\">Age: {totalAge}</span>");
        if (job.Attempts > 1)
        {
            sb.Append($"<span class=\"badge\" style=\"background:var(--warning-light);color:var(--warning);border:1px solid var(--warning)\">🔁 Attempt {job.Attempts}/{effectiveMax}</span>");
        }
        else
        {
            sb.Append($"<span class=\"badge\" style=\"background:var(--bg-tertiary);color:var(--text-tertiary);border:1px solid var(--border)\">Attempt 1/{effectiveMax}</span>");
        }

        if (job.ExpiresAt.HasValue)
        {
            var isExpired = job.Status == JobStatus.Expired || now >= job.ExpiresAt.Value;
            var deadlineText = isExpired
                ? "⚠️ Deadline Expired"
                : $"⏳ Deadline: in {Helpers.CountdownFriendly(job.ExpiresAt.Value - now)}";
            var deadlineStyle = isExpired
                ? "background:rgba(234, 84, 85, 0.15);color:var(--error);border:1px solid var(--error)"
                : "background:rgba(245, 158, 11, 0.15);color:var(--warning);border:1px solid var(--warning)";
            sb.Append($"<span class=\"badge\" style=\"{deadlineStyle}\" title=\"Deadline: {job.ExpiresAt.Value:yyyy-MM-dd HH:mm:ss} UTC\">{deadlineText}</span>");
        }

        sb.Append("</div></div>");

        // 3. Horizontal Stepper
        sb.Append("<div class=\"lifecycle-stepper\">");

        // Node: Scheduled (if applicable)
        if (job.ScheduledAt.HasValue)
        {
            var isPassed = now >= job.ScheduledAt.Value;
            var schedClass = isPassed ? "success" : "active";
            sb.Append($"<div class=\"lifecycle-node {schedClass}\">");
            sb.Append($"<div style=\"display:flex;justify-content:space-between;align-items:center;margin-bottom:4px\"><span style=\"font-size:11px;font-weight:700;color:var(--primary);text-transform:uppercase\">⏰ Scheduled</span><span style=\"font-size:11px;color:var(--text-tertiary);font-family:monospace\">{job.CreatedAt:HH:mm:ss}</span></div>");
            sb.Append($"<div style=\"font-size:13px;font-weight:600;color:var(--text-primary)\">Run at {job.ScheduledAt.Value:HH:mm:ss}</div>");
            sb.Append($"<div style=\"font-size:11px;color:var(--text-secondary)\">{(isPassed ? "Released to queue" : "Due in " + Helpers.CountdownFriendly(job.ScheduledAt.Value - now))}</div>");
            sb.Append("</div>");

            // Connector to Enqueued
            sb.Append("<div class=\"lifecycle-connector\">");
            sb.Append($"<span class=\"lifecycle-metric-badge\" style=\"background:var(--primary-light);color:var(--primary);border:1px solid var(--primary)\">delay</span>");
            sb.Append("<div class=\"lifecycle-arrow\"><svg width=\"24\" height=\"16\" viewBox=\"0 0 24 16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><line x1=\"2\" y1=\"8\" x2=\"20\" y2=\"8\"></line><polyline points=\"14 2 20 8 14 14\"></polyline></svg></div>");
            sb.Append("</div>");
        }

        // Node: Enqueued
        sb.Append("<div class=\"lifecycle-node success\">");
        sb.Append($"<div style=\"display:flex;justify-content:space-between;align-items:center;margin-bottom:4px\"><span style=\"font-size:11px;font-weight:700;color:var(--info);text-transform:uppercase\">📥 Enqueued</span><span style=\"font-size:11px;color:var(--text-tertiary);font-family:monospace\">{enqueuedAt:HH:mm:ss}</span></div>");
        sb.Append($"<div style=\"font-size:13px;font-weight:600;color:var(--text-primary)\">Queue: {HtmlEncode(job.Queue)}</div>");
        sb.Append($"<div style=\"font-size:11px;color:var(--text-secondary)\">Priority: {job.Priority}</div>");
        sb.Append("</div>");

        // Connector: Enqueued -> Processing
        sb.Append("<div class=\"lifecycle-connector\">");
        sb.Append($"<span class=\"lifecycle-metric-badge\" style=\"background:var(--bg-tertiary);color:var(--text-secondary);border:1px solid var(--border)\">{waitTimeStr}</span>");
        sb.Append("<div class=\"lifecycle-arrow\"><svg width=\"24\" height=\"16\" viewBox=\"0 0 24 16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><line x1=\"2\" y1=\"8\" x2=\"20\" y2=\"8\"></line><polyline points=\"14 2 20 8 14 14\"></polyline></svg></div>");
        sb.Append("</div>");

        // Node: Processing
        string procClass;
        if (job.Status == JobStatus.Processing)
        {
            procClass = "active";
        }
        else if (job.Attempts > 1)
        {
            procClass = "warning";
        }
        else if (job.ProcessingStartedAt.HasValue)
        {
            procClass = "success";
        }
        else
        {
            procClass = string.Empty;
        }

        var procTime = job.ProcessingStartedAt.HasValue ? $"{job.ProcessingStartedAt.Value:HH:mm:ss}" : "—";
        var procTitle = job.Status == JobStatus.Processing ? "⚙️ Processing (Live)" : "⚙️ Processing";
        var procDetail = job.Attempts > 1
            ? $"Attempt {job.Attempts} of {effectiveMax} (retried)"
            : $"Attempt {Math.Max(1, job.Attempts)} of {effectiveMax}";

        sb.Append($"<div class=\"lifecycle-node {procClass}\">");
        sb.Append($"<div style=\"display:flex;justify-content:space-between;align-items:center;margin-bottom:4px\"><span style=\"font-size:11px;font-weight:700;color:var(--warning);text-transform:uppercase\">{procTitle}</span><span style=\"font-size:11px;color:var(--text-tertiary);font-family:monospace\">{procTime}</span></div>");
        sb.Append($"<div style=\"font-size:13px;font-weight:600;color:var(--text-primary)\">{procDetail}</div>");
        sb.Append($"<div style=\"font-size:11px;color:var(--text-secondary)\">{(job.ProcessingStartedAt.HasValue ? "Worker slot claimed" : "Waiting for worker")}</div>");
        sb.Append("</div>");

        // Connector: Processing -> Result
        sb.Append("<div class=\"lifecycle-connector\">");
        string resultBadge = !string.Equals(durationStr, "—", StringComparison.Ordinal)
            ? durationStr
            : "outcome";
        sb.Append($"<span class=\"lifecycle-metric-badge\" style=\"background:var(--bg-tertiary);color:var(--text-secondary);border:1px solid var(--border)\">{resultBadge}</span>");
        sb.Append("<div class=\"lifecycle-arrow\"><svg width=\"24\" height=\"16\" viewBox=\"0 0 24 16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><line x1=\"2\" y1=\"8\" x2=\"20\" y2=\"8\"></line><polyline points=\"14 2 20 8 14 14\"></polyline></svg></div>");
        sb.Append("</div>");

        // Node: Outcome
        if (job.Status == JobStatus.Succeeded)
        {
            var compTime = job.CompletedAt.HasValue ? $"{job.CompletedAt.Value:HH:mm:ss}" : "—";
            sb.Append("<div class=\"lifecycle-node success\">");
            sb.Append($"<div style=\"display:flex;justify-content:space-between;align-items:center;margin-bottom:4px\"><span style=\"font-size:11px;font-weight:700;color:var(--success);text-transform:uppercase\">✅ Succeeded</span><span style=\"font-size:11px;color:var(--text-tertiary);font-family:monospace\">{compTime}</span></div>");
            sb.Append("<div style=\"font-size:13px;font-weight:600;color:var(--text-primary)\">Completed with success</div>");
            sb.Append($"<div style=\"font-size:11px;color:var(--text-secondary)\">Execution duration: {durationStr}</div>");
            sb.Append("</div>");
        }
        else if (job.Status == JobStatus.Failed)
        {
            var compTime = job.CompletedAt.HasValue ? $"{job.CompletedAt.Value:HH:mm:ss}" : "—";
            sb.Append("<div class=\"lifecycle-node failed\">");
            sb.Append($"<div style=\"display:flex;justify-content:space-between;align-items:center;margin-bottom:4px\"><span style=\"font-size:11px;font-weight:700;color:var(--error);text-transform:uppercase\">❌ Dead-Letter / Failed</span><span style=\"font-size:11px;color:var(--text-tertiary);font-family:monospace\">{compTime}</span></div>");
            sb.Append("<div style=\"font-size:13px;font-weight:600;color:var(--text-primary)\">Attempts exhausted</div>");
            sb.Append($"<div style=\"font-size:11px;color:var(--text-secondary)\">{job.Attempts}/{effectiveMax} failed attempts</div>");
            sb.Append("</div>");
        }
        else if (job.Status == JobStatus.Expired)
        {
            var expTime = job.ExpiresAt.HasValue ? $"{job.ExpiresAt.Value:HH:mm:ss}" : "—";
            sb.Append("<div class=\"lifecycle-node warning\">");
            sb.Append($"<div style=\"display:flex;justify-content:space-between;align-items:center;margin-bottom:4px\"><span style=\"font-size:11px;font-weight:700;color:var(--warning);text-transform:uppercase\">⚠️ Expired</span><span style=\"font-size:11px;color:var(--text-tertiary);font-family:monospace\">{expTime}</span></div>");
            sb.Append("<div style=\"font-size:13px;font-weight:600;color:var(--text-primary)\">Deadline exceeded</div>");
            sb.Append("<div style=\"font-size:11px;color:var(--text-secondary)\">Skipped before execution</div>");
            sb.Append("</div>");
        }
        else if (job.Status == JobStatus.Enqueued && job.RetryAt.HasValue && job.RetryAt.Value > now)
        {
            var retryTime = $"{job.RetryAt.Value:HH:mm:ss}";
            var waitCountdown = Helpers.CountdownFriendly(job.RetryAt.Value - now);
            sb.Append("<div class=\"lifecycle-node warning\">");
            sb.Append($"<div style=\"display:flex;justify-content:space-between;align-items:center;margin-bottom:4px\"><span style=\"font-size:11px;font-weight:700;color:var(--warning);text-transform:uppercase\">⏳ Awaiting Retry</span><span style=\"font-size:11px;color:var(--text-tertiary);font-family:monospace\">{retryTime}</span></div>");
            sb.Append($"<div style=\"font-size:13px;font-weight:600;color:var(--text-primary)\">Backoff in progress</div>");
            sb.Append($"<div style=\"font-size:11px;color:var(--text-secondary)\">Retrying in {waitCountdown}</div>");
            sb.Append("</div>");
        }
        else if (job.Status == JobStatus.Processing)
        {
            sb.Append("<div class=\"lifecycle-node active\">");
            sb.Append("<div style=\"display:flex;justify-content:space-between;align-items:center;margin-bottom:4px\"><span style=\"font-size:11px;font-weight:700;color:var(--primary);text-transform:uppercase\">🔄 In Progress</span><span class=\"pulse-live\"></span></div>");
            sb.Append("<div style=\"font-size:13px;font-weight:600;color:var(--text-primary)\">Worker executing job</div>");
            sb.Append($"<div style=\"font-size:11px;color:var(--text-secondary)\">{durationStr}</div>");
            sb.Append("</div>");
        }
        else
        {
            sb.Append("<div class=\"lifecycle-node\">");
            sb.Append("<div style=\"display:flex;justify-content:space-between;align-items:center;margin-bottom:4px\"><span style=\"font-size:11px;font-weight:700;color:var(--text-tertiary);text-transform:uppercase\">Outcome</span></div>");
            sb.Append("<div style=\"font-size:13px;font-weight:600;color:var(--text-primary)\">Pending execution</div>");
            sb.Append("<div style=\"font-size:11px;color:var(--text-secondary)\">Awaiting completion</div>");
            sb.Append("</div>");
        }

        sb.Append("</div>"); // Close lifecycle-stepper

        // 4. Retry Loop Diagram (If attempts > 1 or RetryAt active)
        if (job.Attempts > 1 || job.RetryAt.HasValue)
        {
            var isFailed = job.Status == JobStatus.Failed;
            var isSucceeded = job.Status == JobStatus.Succeeded;
            string bannerClass;
            string headerIcon;
            string headerTitle;
            string titleColor;
            string attemptBadgeStyle;

            if (isFailed)
            {
                bannerClass = "retry-loop-banner exhausted";
                headerIcon = "💀";
                headerTitle = "Retry Budget Exhausted — Moved to Dead-Letter";
                titleColor = "var(--error)";
                attemptBadgeStyle = "font-size:11px;background:var(--error-light);color:var(--error);border:1px solid var(--error);padding:2px 8px;border-radius:12px;font-weight:600";
            }
            else if (isSucceeded)
            {
                bannerClass = "retry-loop-banner recovered";
                headerIcon = "🎉";
                headerTitle = "Fault Recovered via Retry Loop";
                titleColor = "var(--success)";
                attemptBadgeStyle = "font-size:11px;background:var(--success-light);color:var(--success);border:1px solid var(--success);padding:2px 8px;border-radius:12px;font-weight:600";
            }
            else
            {
                bannerClass = "retry-loop-banner";
                headerIcon = "🔄";
                headerTitle = "Retry Loop Active &amp; Backoff in Progress";
                titleColor = "var(--warning)";
                attemptBadgeStyle = "font-size:11px;background:var(--warning-light);color:var(--warning);border:1px solid var(--warning);padding:2px 8px;border-radius:12px;font-weight:600";
            }

            sb.Append($"<div class=\"{bannerClass}\">");
            sb.Append($"<div style=\"font-size:24px;line-height:1;flex-shrink:0\">{headerIcon}</div>");
            sb.Append("<div style=\"flex:1\">");
            sb.Append("<div style=\"display:flex;justify-content:space-between;align-items:center;margin-bottom:8px;flex-wrap:wrap;gap:8px\">");
            sb.Append("<div style=\"display:flex;align-items:center;gap:8px\">");
            sb.Append($"<span style=\"font-size:14px;font-weight:700;color:{titleColor}\">{headerTitle}</span>");
            sb.Append($"<span style=\"{attemptBadgeStyle}\">Attempt {job.Attempts} of {effectiveMax}</span>");
            sb.Append("</div>");

            if (isSucceeded)
            {
                sb.Append($"<span style=\"font-size:11px;font-weight:700;color:var(--success);background:var(--success-light);border:1px solid var(--success);padding:2px 10px;border-radius:12px\">🎉 Recovered on Attempt {job.Attempts}</span>");
            }
            else if (isFailed)
            {
                sb.Append("<span style=\"font-size:11px;font-weight:700;color:var(--error);background:var(--error-light);border:1px solid var(--error);padding:2px 10px;border-radius:12px\">💀 Dead-Letter Queue (Exhausted)</span>");
            }
            else if (job.RetryAt.HasValue && job.RetryAt.Value > now)
            {
                sb.Append($"<span style=\"font-size:11px;font-weight:700;color:var(--info);background:var(--info-light);border:1px solid var(--info);padding:2px 10px;border-radius:12px\">⏳ Next Retry in {Helpers.CountdownFriendly(job.RetryAt.Value - now)}</span>");
            }

            sb.Append("</div>");

            // Visual linear diagram of the loop
            sb.Append("<div style=\"background:var(--bg-tertiary);border:1px solid var(--border);border-radius:8px;padding:10px 14px;margin:8px 0;font-size:12px;display:flex;align-items:center;gap:8px;flex-wrap:wrap\">");

            var arrowHtml = "<span style=\"color:var(--text-tertiary);font-size:11px;font-family:monospace\">──►</span>";
            var backoffBadgeHtml = "<span style=\"font-size:11px;padding:2px 8px;border-radius:10px;background:var(--warning-light);color:var(--warning);border:1px solid rgba(245,158,11,0.3);font-weight:600\">Backoff Delay</span>";

            int maxStep = Math.Min(job.Attempts, effectiveMax);
            for (int i = 1; i < maxStep; i++)
            {
                sb.Append($"<span style=\"padding:3px 8px;border-radius:6px;background:rgba(234,84,85,0.12);color:var(--error);border:1px solid rgba(234,84,85,0.3);font-weight:600\">Attempt {i}: Failed</span>");
                sb.Append(arrowHtml);
                sb.Append(backoffBadgeHtml);
                sb.Append(arrowHtml);
            }

            if (isFailed)
            {
                sb.Append($"<span style=\"padding:3px 8px;border-radius:6px;background:rgba(234,84,85,0.18);color:var(--error);border:1px solid var(--error);font-weight:700\">Attempt {maxStep}: Dead-Letter</span>");
            }
            else if (isSucceeded)
            {
                sb.Append($"<span style=\"padding:3px 8px;border-radius:6px;background:rgba(40,199,111,0.15);color:var(--success);border:1px solid var(--success);font-weight:700\">Attempt {maxStep}: Succeeded</span>");
            }
            else if (job.Status == JobStatus.Processing)
            {
                sb.Append($"<span style=\"padding:3px 8px;border-radius:6px;background:rgba(0,207,213,0.15);color:var(--primary);border:1px solid var(--primary);font-weight:700\">Attempt {maxStep}: Running</span>");
            }
            else if (job.RetryAt.HasValue && job.RetryAt.Value > now)
            {
                sb.Append($"<span style=\"padding:3px 8px;border-radius:6px;background:rgba(234,84,85,0.12);color:var(--error);border:1px solid rgba(234,84,85,0.3);font-weight:600\">Attempt {maxStep}: Failed</span>");
                sb.Append(arrowHtml);
                sb.Append(backoffBadgeHtml);
                sb.Append(arrowHtml);
                sb.Append($"<span style=\"padding:3px 8px;border-radius:6px;background:rgba(245,158,11,0.15);color:var(--warning);border:1px solid var(--warning);font-weight:700\">Attempt {maxStep + 1}: Scheduled</span>");
            }
            else
            {
                sb.Append($"<span style=\"padding:3px 8px;border-radius:6px;background:var(--bg-secondary);color:var(--text-secondary);border:1px solid var(--border);font-weight:600\">Attempt {maxStep}</span>");
            }

            sb.Append("</div>");

            if (!string.IsNullOrEmpty(job.LastErrorMessage))
            {
                sb.Append($"<div style=\"font-size:12px;color:var(--error);margin-top:6px;font-family:monospace;background:rgba(234, 84, 85, 0.08);padding:8px 12px;border-radius:6px;border-left:3px solid var(--error)\"><strong>Last Error:</strong> {HtmlEncode(job.LastErrorMessage)}</div>");
            }

            sb.Append("</div></div>");
        }

        // 5. Collapsible Chronological Events Log
        var events = BuildTimelineEvents(job, now).ToList();
        if (events.Count > 0)
        {
            sb.Append("<details style=\"margin-top:18px;border-top:1px solid var(--border);padding-top:14px\">");
            sb.Append("<summary style=\"font-size:12px;font-weight:600;color:var(--text-secondary);cursor:pointer;display:flex;align-items:center;gap:6px\">");
            sb.Append($"<span>📜 Chronological Execution Events ({events.Count})</span>");
            sb.Append("</summary>");

            sb.Append("<div class=\"timeline\" style=\"position:relative;padding-left:32px;margin-top:16px\">");
            for (int i = 0; i < events.Count; i++)
            {
                var @event = events[i];
                var isLast = i == events.Count - 1;
                var color = GetTimelineColor(@event.CssClass);
                var timeStr = @event.At.HasValue ? $"{@event.At.Value:HH:mm:ss}" : "—";

                if (!isLast)
                {
                    sb.Append($"<div style=\"position:absolute;left:11px;top:{(i * 54) + 16}px;bottom:0;width:2px;background:var(--border);height:38px\"></div>");
                }

                sb.Append("<div class=\"timeline-item\" style=\"margin-bottom:18px;position:relative\">");
                sb.Append($"<div style=\"position:absolute;left:-28px;top:4px;width:10px;height:10px;border-radius:50%;background:{color};box-shadow:0 0 0 4px var(--bg-primary)\"></div>");
                sb.Append("<div class=\"timeline-content\">");
                sb.Append($"<div style=\"font-weight:700;font-size:13px;color:var(--text-primary)\">{HtmlEncode(@event.Label)} <span style=\"font-weight:400;color:var(--text-tertiary);float:right;font-size:11px\">{timeStr}</span></div>");
                if (!string.IsNullOrEmpty(@event.Subtitle))
                {
                    sb.Append($"<div style=\"font-size:11px;color:var(--text-secondary)\">{HtmlEncode(@event.Subtitle)}</div>");
                }

                if (!string.IsNullOrEmpty(@event.Error))
                {
                    sb.Append($"<div style=\"font-size:11px;color:var(--error);margin-top:4px;padding:6px 10px;background:var(--error-light);border-radius:4px\">{HtmlEncode(@event.Error)}</div>");
                }

                sb.Append("</div></div>");
            }

            sb.Append("</div></details>");
        }

        sb.Append("</div>"); // Close lifecycle-card
        return sb.ToString();
    }

    private static string GetTimelineColor(string cssClass) => cssClass switch
    {
        "succeeded" => "var(--success)",
        "failed" or "dead" => "var(--error)",
        "processing" => "var(--warning)",
        "scheduled" => "var(--primary)",
        "enqueued" => "var(--info)",
        _ => "var(--text-tertiary)",
    };

    private static IEnumerable<TimelineEvent> BuildTimelineEvents(JobRecord job, DateTimeOffset now)
    {
        var effectiveMax = Helpers.GetEffectiveMaxAttempts(job);
        yield return new TimelineEvent(job.CreatedAt, "Enqueued", "enqueued", $"queue: {HtmlEncode(job.Queue)} · priority: {job.Priority}", null);
        if (job.ProcessingStartedAt.HasValue)
        {
            yield return new TimelineEvent(job.ProcessingStartedAt.Value, "Processing", "processing", $"attempt 1/{effectiveMax}", null);
            if (job.Attempts > 1)
            {
                for (int i = 2; i <= job.Attempts; i++)
                {
                    yield return new TimelineEvent(job.CompletedAt, "Failed", "failed", null, job.LastErrorMessage);
                    if (job.RetryAt.HasValue && i == job.Attempts)
                    {
                        yield return new TimelineEvent(job.RetryAt.Value, "Retry scheduled", "scheduled", Helpers.CountdownFriendly(job.RetryAt.Value - now), null);
                        if (job.RetryAt.Value <= now)
                        {
                            yield return new TimelineEvent(job.RetryAt.Value, "Processing", "processing", $"attempt {i}/{effectiveMax}", null);
                        }
                    }
                }
            }
        }

        if (job.Status == JobStatus.Succeeded && job.CompletedAt.HasValue)
        {
            yield return new TimelineEvent(job.CompletedAt.Value, "Succeeded", "succeeded", null, null);
        }
        else if (job.Status == JobStatus.Failed && job.CompletedAt.HasValue)
        {
            if (!job.RetryAt.HasValue || job.RetryAt.Value <= now)
            {
                yield return new TimelineEvent(job.CompletedAt.Value, "Failed", "failed", null, job.LastErrorMessage);
                yield return new TimelineEvent(job.CompletedAt.Value, "Dead-letter", "dead", "handler invoked", null);
            }
        }
        else if (job.Status == JobStatus.Expired && job.ExpiresAt.HasValue)
        {
            yield return new TimelineEvent(job.ExpiresAt.Value, "Expired", "expired", "deadline passed before execution", null);
        }
    }

    /// <summary>Renders the visual cluster topology flowchart (Listeners -> Queues -> Workers).</summary>
    internal static string TopologyMap(
        IReadOnlyList<ListenerSnapshot>? listeners,
        IReadOnlyList<QueueMetrics>? queues,
        IReadOnlyList<ServerRecord>? servers,
        string pathPrefix)
    {
        var listenersCount = listeners?.Count ?? 0;
        var queuesCount = queues?.Count ?? 0;
        var workersCount = servers?.Sum(s => s.WorkerCount) ?? 0;

        var listenersHtml = new StringBuilder();
        if (listeners != null && listeners.Count > 0)
        {
            foreach (var l in listeners.Take(3))
            {
                listenersHtml.Append("<div class=\"topo-box\">")
                    .Append("<div class=\"topo-title\"><span>").Append(HtmlEncode(l.Broker)).Append("</span><span class=\"pulse-live\"></span></div>")
                    .Append("<div class=\"topo-val\">").Append(HtmlEncode(l.Endpoint)).Append("</div>")
                    .Append("<div class=\"topo-sub\">➔ ").Append(HtmlEncode(Helpers.ShortType(l.TargetJobType))).Append("</div>")
                    .Append("</div>");
            }
        }
        else
        {
            listenersHtml.Append("<div class=\"topo-box\"><div class=\"topo-title\">Triggers / Brokers</div><div class=\"topo-val\">Direct Enqueue / Cron</div><div class=\"topo-sub\">No external listeners</div></div>");
        }

        var queuesHtml = new StringBuilder();
        if (queues != null && queues.Count > 0)
        {
            foreach (var q in queues.Take(3))
            {
                var count = q.Enqueued + q.Processing;
                queuesHtml.Append("<div class=\"topo-box\">")
                    .Append("<div class=\"topo-title\">Queue: ").Append(HtmlEncode(q.Queue)).Append("</div>")
                    .Append("<div class=\"topo-val\">").Append(count).Append(" active</div>")
                    .Append("<div class=\"topo-sub\">").Append(q.Enqueued).Append(" waiting · ").Append(q.Processing).Append(" running</div>")
                    .Append("</div>");
            }
        }
        else
        {
            queuesHtml.Append("<div class=\"topo-box\"><div class=\"topo-title\">Queue: default</div><div class=\"topo-val\">Idle</div><div class=\"topo-sub\">0 waiting · 0 running</div></div>");
        }

        var workersHtml = new StringBuilder();
        if (servers != null && servers.Count > 0)
        {
            foreach (var s in servers.Take(3))
            {
                workersHtml.Append("<div class=\"topo-box\">")
                    .Append("<div class=\"topo-title\"><span>Worker Node</span><span class=\"pulse-live\"></span></div>")
                    .Append("<div class=\"topo-val\" title=\"").Append(HtmlAttributeEncode(s.Id)).Append("\">").Append(HtmlEncode(Helpers.FormatServerId(s.Id))).Append("</div>")
                    .Append("<div class=\"topo-sub\">").Append(s.WorkerCount).Append(" slots active</div>")
                    .Append("</div>");
            }
        }
        else
        {
            workersHtml.Append("<div class=\"topo-box\"><div class=\"topo-title\">Worker Nodes</div><div class=\"topo-val\">Offline</div><div class=\"topo-sub\">No active workers</div></div>");
        }

        const string ArrowSvg =
            """
            <div class="topo-arrow">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                    <line x1="5" y1="12" x2="19" y2="12"></line>
                    <polyline points="12 5 19 12 12 19"></polyline>
                </svg>
            </div>
            """;

        return
            $$"""
            <div class="topology-card">
                <div class="topology-header">
                    <div>
                        <h3 style="font-size:16px;font-weight:600;margin:0 0 4px 0">Cluster Pipeline Topology</h3>
                        <p style="font-size:12px;color:var(--text-secondary);margin:0">Live event flow from external broker triggers through buffer queues to background worker executors</p>
                    </div>
                    <div style="display:flex;gap:8px">
                        <a href="{{pathPrefix}}/listeners" class="btn btn-secondary btn-sm">{{listenersCount}} Triggers</a>
                        <a href="{{pathPrefix}}/queues" class="btn btn-secondary btn-sm">{{queuesCount}} Queues</a>
                        <a href="{{pathPrefix}}/servers" class="btn btn-secondary btn-sm">{{workersCount}} Workers</a>
                    </div>
                </div>
                <div class="topology-diagram">
                    <div class="topo-col">
                        <div style="font-size:11px;font-weight:700;color:var(--text-tertiary);text-transform:uppercase;margin-bottom:2px">1. Ingress & Triggers</div>
                        {{listenersHtml}}
                    </div>
                    {{ArrowSvg}}
                    <div class="topo-col">
                        <div style="font-size:11px;font-weight:700;color:var(--text-tertiary);text-transform:uppercase;margin-bottom:2px">2. Queue Buffers</div>
                        {{queuesHtml}}
                    </div>
                    {{ArrowSvg}}
                    <div class="topo-col">
                        <div style="font-size:11px;font-weight:700;color:var(--text-tertiary);text-transform:uppercase;margin-bottom:2px">3. Processing Workers</div>
                        {{workersHtml}}
                    </div>
                </div>
            </div>
            """;
    }

    private static string HtmlEncode(string? text) => HttpUtility.HtmlEncode(text ?? string.Empty);

    private static string HtmlAttributeEncode(string? text) => HttpUtility.HtmlAttributeEncode(text ?? string.Empty);

    private sealed record TimelineEvent(DateTimeOffset? At, string Label, string CssClass, string? Subtitle, string? Error);
}
