using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Web;
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

    /// <summary>Renders a standard empty state with optional SVG icon and message.</summary>
    internal static string EmptyState(string svgPath, string message) =>
        $"""
        <div class="card" style="padding:48px;text-align:center;color:var(--text-tertiary)">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1" style="margin-bottom:16px"><path d="{svgPath}"/></svg>
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

        var attemptInfo = job.Attempts > 1
            ? $" <span style=\"color:var(--text-secondary);font-size:11px\">({job.Attempts}/{job.MaxAttempts})</span>"
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

    /// <summary>Renders a job row for the Overview page recent failures section.</summary>
    internal static string JobRowOverview(JobRecord job, string pathPrefix, DateTimeOffset now) =>
        $"<a href=\"{pathPrefix}/jobs/{job.Id.Value}\" style=\"text-decoration:none\">" +
        $"<div class=\"job-row\" style=\"grid-template-columns: 32px 1fr 100px; padding: 12px 20px; border:none; border-bottom:1px solid var(--border)\">" +
        $"<div class=\"job-row-dot\">{Helpers.StatusDot(JobStatus.Failed)}</div>" +
        $"<div class=\"job-row-main\">" +
        $"<div class=\"job-row-title\" style=\"font-weight:600\">{HtmlEncode(Helpers.ShortType(job.JobType))}</div>" +
        $"<div class=\"job-row-sub\" style=\"font-size:11px;color:var(--error);white-space:nowrap;overflow:hidden;text-overflow:ellipsis\">{HtmlEncode(Helpers.Truncate(job.LastErrorMessage, 60))}</div>" +
        $"</div>" +
        $"<div class=\"job-row-meta\" style=\"text-align:right;font-size:11px;color:var(--text-secondary)\">{Helpers.RelativeTime(job.CompletedAt, now)}</div>" +
        $"</div></a>";

    /// <summary>Renders status filter pills for the Jobs page.</summary>
    internal static string StatusPills(string currentStatus, string baseUrl)
    {
        var pills = string.Join(string.Empty, new[]
        {
            (string.Empty, "All"),
            ("Enqueued", "Enqueued"),
            ("Processing", "Processing"),
            ("Succeeded", "Succeeded"),
            ("Failed", "Failed"),
            ("Scheduled", "Scheduled"),
        }.Select(o =>
        {
            var active = string.Equals(currentStatus, o.Item1, StringComparison.Ordinal) ? " active" : string.Empty;
            var qs = $"?status={Uri.EscapeDataString(o.Item1)}";
            return $"<a href=\"{baseUrl}{qs}\" class=\"nav-item{active}\" style=\"padding:6px 12px;font-size:12px\">{o.Item2}</a>";
        }));
        return $"<div style=\"display:flex;gap:4px;margin-bottom:16px\">{pills}</div>";
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

        return
            $"<div class=\"filters\">" +
            $"<form method=\"get\" action=\"{pathPrefix}/jobs\" style=\"display:flex;gap:8px;margin-bottom:16px;flex-wrap:wrap;width:100%\">" +
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

    /// <summary>Renders a detail section with header and key-value grid.</summary>
    internal static string DetailSection(string sectionTitle, params (string Label, string Value)[] rows) =>
        $"<div class=\"card\" style=\"margin-bottom:24px\">" +
        $"<div class=\"card-header\"><h3>{HtmlEncode(sectionTitle)}</h3></div>" +
        $"<div style=\"padding:20px;display:grid;grid-template-columns:repeat(auto-fit, minmax(300px, 1fr));gap:16px\">" +
        string.Join(string.Empty, rows.Select(r => DetailRow(r.Label, r.Value))) +
        $"</div></div>";

    /// <summary>Renders a single key-value pair in a detail grid.</summary>
    internal static string DetailRow(string label, string value) =>
        $"<div style=\"display:flex;flex-direction:column;gap:4px\">" +
        $"<div style=\"font-size:11px;font-weight:700;color:var(--text-secondary);text-transform:uppercase\">{HtmlEncode(label)}</div>" +
        $"<div style=\"font-size:14px;color:var(--text-primary)\">{value}</div>" +
        $"</div>";

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

    /// <summary>Renders the execution logs section in terminal style.</summary>
    internal static string LogsSection(IReadOnlyList<JobExecutionLog> logs)
    {
        if (logs.Count == 0)
        {
            return
                $"<div style=\"margin-bottom:24px\">" +
                $"<h3 style=\"margin-bottom:8px;font-size:14px;font-weight:600\">Execution Logs</h3>" +
                $"<p style=\"color:var(--text-tertiary);font-size:13px\">No logs captured for this execution.</p>" +
                $"</div>";
        }

        var logLines = string.Join(string.Empty, logs.Select(entry =>
        {
            var color = entry.Level switch
            {
                "Warning" => "var(--warning)",
                "Error" or "Critical" => "var(--error)",
                "Debug" or "Trace" => "var(--text-tertiary)",
                _ => "var(--text-secondary)",
            };
            var ts = entry.Timestamp.ToString("HH:mm:ss.fff");
            var msg = HtmlEncode(entry.Message).Replace("\n", "&#10;");
            return $"<div style=\"display:flex;gap:10px;line-height:1.6\"><span style=\"color:#94a3b8;flex-shrink:0\">[{ts}]</span><span style=\"color:{color};font-weight:600;min-width:70px;flex-shrink:0\">[{entry.Level}]</span><span style=\"color:#e2e8f0;word-break:break-all\">{msg}</span></div>";
        }));

        return
            $"<div style=\"margin-bottom:24px\">" +
            $"<div style=\"display:flex;justify-content:space-between;align-items:center;margin-bottom:8px\">" +
            $"<h3 style=\"font-size:14px;font-weight:600;margin:0\">Execution Logs <span style=\"font-weight:400;color:var(--text-tertiary)\">({logs.Count} entries)</span></h3>" +
            $"</div>" +
            $"<div class=\"terminal-window\">" +
            $"<div class=\"terminal-header\"><div class=\"terminal-dots\"><span></span><span></span><span></span></div><span class=\"terminal-title\">console.log</span><button class=\"copy-btn\" onclick=\"navigator.clipboard.writeText(this.parentElement.nextElementSibling.innerText);this.textContent='Copied!';setTimeout(()=>this.textContent='Copy',1500)\">Copy</button></div>" +
            $"<div class=\"terminal-body\" style=\"max-height:360px;overflow-y:auto;padding:12px 16px;font-family:monospace;font-size:12px\">{logLines}</div>" +
            $"</div>" +
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

    /// <summary>Renders a compact queue row for high-density monitoring with control actions.</summary>
    internal static string QueueCard(QueueMetrics queue, string pathPrefix, bool isPaused = false, DashboardCluster? activeCluster = null)
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
            pauseForm = isPaused
                ? $"<form method=\"post\" action=\"{pathPrefix}/queues/{Uri.EscapeDataString(queue.Queue)}/resume{clusterSuffix}\" style=\"display:inline\">" +
                  $"<button type=\"submit\" class=\"btn btn-primary btn-sm\" title=\"Resume Queue\">▶ Resume</button></form>"
                : $"<form method=\"post\" action=\"{pathPrefix}/queues/{Uri.EscapeDataString(queue.Queue)}/pause{clusterSuffix}\" style=\"display:inline\" onclick=\"return confirm('Pause queue {HtmlEncode(queue.Queue)}?')\">" +
                  $"<button type=\"submit\" class=\"btn btn-secondary btn-sm\" title=\"Pause Queue\">⏸ Pause</button></form>";
        }

        var statusBadge = isPaused
            ? " <span class=\"badge badge-warning\" style=\"font-size:10px;margin-left:6px\">PAUSED</span>"
            : string.Empty;

        return
            $"<div style=\"padding:16px 24px;display:flex;align-items:center;gap:24px;border-bottom:1px solid var(--border)\">" +
            $"<div style=\"width:220px;font-weight:600;font-size:15px;color:var(--text-primary);overflow:hidden;text-overflow:ellipsis;white-space:nowrap\">" +
            $"{HtmlEncode(queue.Queue)}{statusBadge}</div>" +
            $"<div style=\"flex:1;display:flex;gap:40px;align-items:center\">" +
                $"<div style=\"width:150px;display:flex;align-items:baseline;gap:8px\"><div style=\"font-size:10px;color:var(--text-tertiary);font-weight:700\">ENQUEUED</div><div style=\"font-weight:700;color:var(--info);font-size:18px\">{queue.Enqueued}</div></div>" +
                $"<div style=\"width:150px;display:flex;align-items:baseline;gap:8px\"><div style=\"font-size:10px;color:var(--text-tertiary);font-weight:700\">PROCESSING</div><div style=\"font-weight:700;color:var(--warning);font-size:18px\">{queue.Processing}</div></div>" +
                $"<div style=\"flex:1\">" +
                    $"<div style=\"display:flex;justify-content:space-between;margin-bottom:4px\"><span style=\"font-size:10px;color:var(--text-tertiary);font-weight:700\">UTILIZATION</span><span style=\"font-size:10px;font-weight:700;color:{utilColor}\">{utilPct}%</span></div>" +
                    $"<div style=\"height:6px;background:var(--bg-tertiary);border-radius:3px;overflow:hidden\"><div style=\"width:{utilPct}%;background:{utilColor};height:100%;transition:width 0.3s ease\"></div></div>" +
                $"</div>" +
            $"</div>" +
            $"<div style=\"display:flex;gap:8px;align-items:center;justify-content:flex-end\">" +
                pauseForm +
                $"<a href=\"{pathPrefix}/jobs?queue={Uri.EscapeDataString(queue.Queue)}{(activeCluster is not null ? $"&cluster={Uri.EscapeDataString(activeCluster.Id)}" : string.Empty)}\" class=\"btn btn-secondary btn-sm\">View Jobs</a>" +
            $"</div>" +
            $"</div>";
    }

    /// <summary>Renders a high-density table row for a recurring job.</summary>
    internal static string RecurringRow(RecurringJobRecord job, string pathPrefix, DateTimeOffset now, DashboardCluster? activeCluster = null)
    {
        var effectiveCron = job.CronOverride ?? job.Cron;
        var encodedIdUrl = Uri.EscapeDataString(job.RecurringJobId);
        var clusterSuffix = activeCluster is not null ? $"?cluster={Uri.EscapeDataString(activeCluster.Id)}" : string.Empty;
        var isReadOnly = activeCluster?.IsReadOnly == true;

        var rowStyle = !job.Enabled ? "style=\"opacity:0.7;background:var(--bg-secondary)\"" : string.Empty;
        var statusLabel = job.Enabled ? "<span class=\"badge badge-success\">Active</span>" : "<span class=\"badge badge-warning\">Paused</span>";
        if (job.DeletedByUser)
        {
            statusLabel = "<span class=\"badge badge-error\">Deleted</span>";
        }

        var dotClass = !job.Enabled ? "dot-processing" : "dot-succeeded";
        if (job.DeletedByUser)
        {
            dotClass = "dot-failed";
        }

        var statusDot = $"<span class=\"dot {dotClass}\"></span>";

        string nextHtml = "—";
        if (!job.DeletedByUser && job.Enabled && job.NextExecution.HasValue)
        {
            if (job.NextExecution.Value <= now)
            {
                nextHtml = "<span class=\"badge badge-warning\" style=\"font-size:10px;animation:pulse 2s infinite\">DUE NOW</span>";
            }
            else
            {
                nextHtml = $"<span style=\"color:var(--primary);font-weight:500\">in {Helpers.FormatCountdown(job.NextExecution.Value - now)}</span>";
            }
        }

        // Last run
        string lastRunHtml = "<span style=\"color:var(--text-tertiary)\">never</span>";
        if (job.LastExecutedAt.HasValue)
        {
            var isSuccess = job.LastExecutionStatus == JobStatus.Succeeded;
            var badgeClass = isSuccess ? "badge-success" : "badge-error";
            var statusText = job.LastExecutionStatus?.ToString() ?? (isSuccess ? "Succeeded" : "Failed");

            lastRunHtml = $"<div style=\"display:flex;align-items:center;gap:8px;white-space:nowrap\">" +
                          $"<span class=\"badge {badgeClass}\" style=\"font-size:10px;padding:2px 6px;line-height:1\">{statusText}</span>" +
                          $"<span style=\"font-size:11px;color:var(--text-tertiary)\">{Helpers.RelativeTime(job.LastExecutedAt, now)}</span>" +
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
                ? $"<form method=\"post\" action=\"{pathPrefix}/recurring/{encodedIdUrl}/pause{clusterSuffix}\" style=\"display:inline\" onclick=\"event.stopPropagation()\"><button type=\"submit\" class=\"btn-icon-sm\" title=\"Pause\" style=\"color:var(--warning);background:transparent;border:none\">{pauseIcon}</button></form>"
                : $"<form method=\"post\" action=\"{pathPrefix}/recurring/{encodedIdUrl}/resume{clusterSuffix}\" style=\"display:inline\" onclick=\"event.stopPropagation()\"><button type=\"submit\" class=\"btn-icon-sm\" title=\"Resume\" style=\"color:var(--success);background:transparent;border:none\">{playIcon}</button></form>";

            actionsHtml = $"<form method=\"post\" action=\"{pathPrefix}/recurring/{encodedIdUrl}/trigger{clusterSuffix}\" style=\"display:inline\" onclick=\"event.stopPropagation()\"><button type=\"submit\" class=\"btn-icon-sm\" title=\"Trigger Now\" style=\"color:var(--primary);background:transparent;border:none\">{boltIcon}</button></form> {pauseResume}";
        }

        var actionsTd = !isReadOnly
            ? $"<td style=\"padding:12px 24px;text-align:right\"><div style=\"display:flex;gap:4px;justify-content:flex-end\">{actionsHtml}</div></td>"
            : string.Empty;

        return
            $"<tr class=\"table-recurring\" {rowStyle} onclick=\"window.location.href='{pathPrefix}/recurring/{encodedIdUrl}{clusterSuffix}'\" style=\"cursor:pointer\">" +
            $"<td style=\"padding:12px 24px\"><div style=\"display:flex;align-items:center;gap:8px\">{statusDot}{statusLabel}</div></td>" +
            $"<td style=\"padding:12px 24px\"><div style=\"font-weight:600;color:var(--primary)\">{HtmlEncode(job.RecurringJobId)}</div></td>" +
            $"<td style=\"padding:12px 24px\"><span style=\"font-size:12px;color:var(--text-tertiary)\">{HtmlEncode(Helpers.ShortType(job.JobType))}</span></td>" +
            $"<td style=\"padding:12px 24px\"><code style=\"background:var(--bg-tertiary);padding:2px 6px;border-radius:4px;font-size:12px\">{HtmlEncode(effectiveCron)}</code></td>" +
            $"<td style=\"padding:12px 24px;font-size:13px\">{HtmlEncode(job.Queue)}</td>" +
            $"<td style=\"padding:12px 24px\">{lastRunHtml}</td>" +
            $"<td style=\"padding:12px 24px;font-size:13px\">{nextHtml}</td>" +
            actionsTd +
            $"</tr>";
    }

    /// <summary>Renders a server table row.</summary>
    internal static string ServerRow(ServerRecord server, DateTimeOffset now)
    {
        var uptime = server.HeartbeatAt - server.StartedAt;
        var heartbeatAge = now - server.HeartbeatAt;
        var heartbeatStatus = heartbeatAge switch
        {
            var d when d.TotalSeconds < 60 => $"<span class=\"dot dot-succeeded\"></span> Active",
            var d when d.TotalMinutes < 5 => $"<span class=\"dot dot-processing\"></span> Recent",
            _ => $"<span class=\"dot dot-failed\"></span> Offline",
        };

        return
            $"<tr>" +
            $"<td style=\"font-family:monospace;font-size:11px;color:var(--text-tertiary)\">{server.Id}</td>" +
            $"<td>{Helpers.RelativeTime(server.StartedAt, now)}</td>" +
            $"<td>{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m</td>" +
            $"<td><span style=\"color:var(--primary);font-weight:700\">{server.WorkerCount}</span></td>" +
            $"<td>{HtmlEncode(string.Join(", ", server.Queues))}</td>" +
            $"<td>{heartbeatStatus}</td>" +
            $"</tr>";
    }

    /// <summary>Renders pagination controls for recurring job executions.</summary>
    internal static string RecurringJobPagination(PagedResult<JobRecord> result, string pathPrefix, string encodedJobId, int pageSize)
    {
        var totalPages = (int)Math.Ceiling((double)result.TotalCount / result.PageSize);
        if (totalPages <= 1)
        {
            return string.Empty;
        }

        var prev = result.Page > 1
            ? $"<a href=\"{pathPrefix}/recurring/{encodedJobId}?page={result.Page - 1}&pageSize={pageSize}\" class=\"btn btn-secondary btn-sm\">← Prev</a>"
            : string.Empty;

        var next = result.Page < totalPages
            ? $"<a href=\"{pathPrefix}/recurring/{encodedJobId}?page={result.Page + 1}&pageSize={pageSize}\" class=\"btn btn-secondary btn-sm\">Next →</a>"
            : string.Empty;

        return $"<div class=\"pagination\" style=\"display:flex;align-items:center;gap:12px;margin-top:16px\">{prev}{next}<span class=\"page-info\" style=\"font-size:12px;color:var(--text-tertiary)\">Page {result.Page} of {totalPages} ({result.TotalCount} total)</span></div>";
    }

    /// <summary>Returns the read-only mode warning banner HTML.</summary>
    internal static string ReadOnlyBanner() => ReadOnlyBannerHtml;

    /// <summary>Renders a visual execution flow timeline.</summary>
    internal static string ExecutionTimeline(JobRecord job, DateTimeOffset now)
    {
        var events = BuildTimelineEvents(job, now).ToList();
        var sb = new System.Text.StringBuilder();
        sb.Append("<div class=\"timeline\" style=\"position:relative;padding-left:32px\">");

        for (int i = 0; i < events.Count; i++)
        {
            var @event = events[i];
            var isLast = i == events.Count - 1;
            var color = GetTimelineColor(@event.CssClass);
            var timeStr = @event.At.HasValue ? $"{@event.At.Value:HH:mm:ss}" : "—";

            if (!isLast)
            {
                sb.Append($"<div style=\"position:absolute;left:11px;top:{(i * 60) + 16}px;bottom:0;width:2px;background:var(--border);height:44px\"></div>");
            }

            sb.Append($"<div class=\"timeline-item\" style=\"margin-bottom:24px;position:relative\">");
            sb.Append($"<div style=\"position:absolute;left:-28px;top:4px;width:10px;height:10px;border-radius:50%;background:{color};box-shadow:0 0 0 4px var(--bg-primary)\"></div>");
            sb.Append($"<div class=\"timeline-content\">");
            sb.Append($"<div style=\"font-weight:700;font-size:13px;color:var(--text-primary)\">{HtmlEncode(@event.Label)} <span style=\"font-weight:400;color:var(--text-tertiary);float:right;font-size:11px\">{timeStr}</span></div>");
            if (!string.IsNullOrEmpty(@event.Subtitle))
            {
                sb.Append($"<div style=\"font-size:11px;color:var(--text-secondary)\">{HtmlEncode(@event.Subtitle)}</div>");
            }

            if (!string.IsNullOrEmpty(@event.Error))
            {
                sb.Append($"<div style=\"font-size:11px;color:var(--error);margin-top:4px;padding:8px;background:var(--error-light);border-radius:4px\">{HtmlEncode(@event.Error)}</div>");
            }

            sb.Append($"</div></div>");
        }

        sb.Append("</div>");
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
        yield return new TimelineEvent(job.CreatedAt, "Enqueued", "enqueued", $"queue: {HtmlEncode(job.Queue)} · priority: {job.Priority}", null);
        if (job.ProcessingStartedAt.HasValue)
        {
            yield return new TimelineEvent(job.ProcessingStartedAt.Value, "Processing", "processing", $"attempt 1/{job.MaxAttempts}", null);
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
                            yield return new TimelineEvent(job.RetryAt.Value, "Processing", "processing", $"attempt {i}/{job.MaxAttempts}", null);
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
                    .Append("<div class=\"topo-val\">").Append(HtmlEncode(s.Id)).Append("</div>")
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

    private sealed record TimelineEvent(DateTimeOffset? At, string Label, string CssClass, string? Subtitle, string? Error);
}
