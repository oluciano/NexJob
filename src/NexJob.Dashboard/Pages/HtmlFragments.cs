using System.Web;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

/// <summary>Reusable HTML fragment builders for dashboard pages.</summary>
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

    /// <summary>Renders a page header with title, subtitle, and optional action buttons.</summary>
    internal static string PageHeader(string title, string subtitle, string? actionsHtml = null) =>
        $"""
        <div class="page-header">
            <div>
                <h1 class="page-title">{HtmlEncode(title)}</h1>
                <p class="page-subtitle">{HtmlEncode(subtitle)}</p>
            </div>
            {(actionsHtml != null ? $"<div class=\"page-header-actions\">{actionsHtml}</div>" : string.Empty)}
        </div>
        """;

    /// <summary>Renders a standard empty state with optional SVG icon and message.</summary>
    internal static string EmptyState(string svgPath, string message) =>
        $"""
        <div class="empty-state">
            <svg width="40" height="40" viewBox="{svgPath}" fill="none" stroke="currentColor" stroke-width="1">{svgPath}</svg>
            <p>{HtmlEncode(message)}</p>
        </div>
        """;

    /// <summary>Renders a job row for the Jobs list page.</summary>
    internal static string JobRow(JobRecord job, string pathPrefix, DateTimeOffset now)
    {
        var timeCell = job.Status switch
        {
            JobStatus.Scheduled =>
                job.ScheduledAt.HasValue
                    ? $"<span style=\"color:var(--accent-light)\">{Helpers.CountdownFriendly(job.ScheduledAt.Value - now)}</span>"
                    : "—",
            JobStatus.Succeeded or JobStatus.Failed =>
                Helpers.RelativeTime(job.CompletedAt, now),
            JobStatus.Processing =>
                $"<span style=\"color:var(--warning)\">running</span>",
            _ => "—",
        };

        var tagBadges = job.Tags.Count > 0
            ? string.Join(" ", job.Tags.Select(t =>
                $"<a href=\"{pathPrefix}/jobs?tag={Uri.EscapeDataString(t)}\" class=\"tag-badge\">{HtmlEncode(t)}</a>"))
            : string.Empty;

        var tagsRow = tagBadges.Length > 0
            ? $"<div class=\"job-row-tags\">{tagBadges}</div>"
            : string.Empty;

        var attemptInfo = job.Attempts > 0
            ? $"<span>attempt {job.Attempts}/{job.MaxAttempts}</span>"
            : string.Empty;

        return
            $"<a href=\"{pathPrefix}/jobs/{job.Id.Value}\" style=\"text-decoration:none\">" +
            $"<div class=\"job-row\">" +
            $"<div class=\"job-row-dot\">{Helpers.StatusDot(job.Status)}</div>" +
            $"<div class=\"job-row-main\">" +
            $"<div class=\"job-row-title\">{HtmlEncode(Helpers.ShortType(job.JobType))}</div>" +
            $"<div class=\"job-row-sub\">" +
            $"<span style=\"font-family:monospace;font-size:11px;color:var(--text-3)\">{job.Id.Value.ToString()[..8]}…</span>" +
            $"<a href=\"{pathPrefix}/jobs?queue={Uri.EscapeDataString(job.Queue)}\" style=\"color:inherit;text-decoration:none\" onclick=\"event.stopPropagation()\">{HtmlEncode(job.Queue)}</a>" +
            $"<span>priority {job.Priority}</span>" +
            (attemptInfo.Length > 0 ? $"{attemptInfo}" : string.Empty) +
            $"</div>" +
            tagsRow +
            $"</div>" +
            $"<div class=\"job-row-meta\">{timeCell}<br/><span style=\"font-size:11px\">{job.CreatedAt:MM/dd HH:mm}</span></div>" +
            $"</div></a>";
    }

    /// <summary>Renders a job row for the Failed Jobs page (with error snippet).</summary>
    internal static string JobRowFailed(JobRecord job, string pathPrefix, DateTimeOffset now)
    {
        var errorSnippet = Helpers.Truncate(job.LastErrorMessage, 90);
        return
            $"<a href=\"{pathPrefix}/jobs/{job.Id.Value}\" style=\"text-decoration:none\">" +
            $"<div class=\"job-row\">" +
            $"<div class=\"job-row-dot\">{Helpers.StatusDot(JobStatus.Failed)}</div>" +
            $"<div class=\"job-row-main\">" +
            $"<div class=\"job-row-title\">{HtmlEncode(Helpers.ShortType(job.JobType))}</div>" +
            $"<div class=\"job-row-sub\">" +
            $"<span style=\"font-family:monospace;font-size:11px;color:var(--text-3)\">{job.Id.Value.ToString()[..8]}…</span>" +
            $"<span>{HtmlEncode(job.Queue)}</span>" +
            $"<span>attempt {job.Attempts}/{job.MaxAttempts}</span>" +
            $"</div>" +
            $"<div style=\"font-size:12px;color:var(--danger);margin-top:4px\">{HtmlEncode(errorSnippet)}</div>" +
            $"</div>" +
            $"<div class=\"job-row-meta\">" +
            $"{Helpers.RelativeTime(job.CompletedAt, now)}" +
            $"</div>" +
            $"</div></a>";
    }

    /// <summary>Renders a job row for the Overview page recent failures section.</summary>
    internal static string JobRowOverview(JobRecord job, string pathPrefix, DateTimeOffset now) =>
        $"<a href=\"{pathPrefix}/jobs/{job.Id.Value}\" style=\"text-decoration:none\">" +
        $"<div class=\"job-row\" style=\"margin-bottom:0\">" +
        $"<div class=\"job-row-dot\">{Helpers.StatusDot(JobStatus.Failed)}</div>" +
        $"<div class=\"job-row-main\">" +
        $"<div class=\"job-row-title\">{HtmlEncode(Helpers.ShortType(job.JobType))}</div>" +
        $"<div class=\"job-row-sub\">" +
        $"<span>{HtmlEncode(job.Queue)}</span>" +
        $"<span style=\"color:var(--danger);font-size:11px\">{HtmlEncode(Helpers.Truncate(job.LastErrorMessage, 80))}</span>" +
        $"</div></div>" +
        $"<div class=\"job-row-meta\">{Helpers.RelativeTime(job.CompletedAt, now)}</div>" +
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
            var active = currentStatus == o.Item1 ? " active" : string.Empty;
            var qs = $"?status={Uri.EscapeDataString(o.Item1)}";
            return $"<a href=\"{baseUrl}{qs}\" class=\"status-pill{active}\">{o.Item2}</a>";
        }));
        return $"<div class=\"status-pills\">{pills}</div>";
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
            var active = currentStatus == o.Item1 ? " active" : string.Empty;
            var qs = $"?status={Uri.EscapeDataString(o.Item1)}";
            return $"<a href=\"{baseUrl}{qs}\" class=\"status-pill{active}\">{o.Item2}</a>";
        }));
        return $"<div class=\"status-pills\">{pills}</div>";
    }

    /// <summary>Renders the filter bar for the Jobs page with search, tag, queue, and status pills.</summary>
    internal static string FilterBar(string pathPrefix, string currentStatus, string? search, string? tag, string? queue = null)
    {
        var searchVal = HttpUtility.HtmlAttributeEncode(search ?? string.Empty);
        var tagVal = HttpUtility.HtmlAttributeEncode(tag ?? string.Empty);
        var queueVal = HttpUtility.HtmlAttributeEncode(queue ?? string.Empty);
        var pills = StatusPills(currentStatus, $"{pathPrefix}/jobs");

        return
            $"<div class=\"filters\">" +
            $"<form method=\"get\" action=\"{pathPrefix}/jobs\" style=\"display:contents\">" +
            $"<input type=\"text\" name=\"search\" placeholder=\"Search type or ID…\" value=\"{searchVal}\" />" +
            $"<input type=\"text\" name=\"queue\" placeholder=\"Queue…\" value=\"{queueVal}\" style=\"min-width:100px\" />" +
            $"<input type=\"text\" name=\"tag\" placeholder=\"Tag…\" value=\"{tagVal}\" style=\"min-width:130px\" />" +
            $"<input type=\"hidden\" name=\"status\" value=\"{HttpUtility.HtmlAttributeEncode(currentStatus)}\" />" +
            $"<button type=\"submit\" class=\"btn btn-ghost btn-sm\">Search</button>" +
            (searchVal.Length > 0 || tagVal.Length > 0 || queueVal.Length > 0
                ? $"<a href=\"{pathPrefix}/jobs\" class=\"btn btn-ghost btn-sm\">Clear</a>"
                : string.Empty) +
            $"</form>" +
            pills +
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
            ? $"<a href=\"{baseUrl}&page={result.Page - 1}\" class=\"btn btn-ghost btn-sm\">← Prev</a>"
            : "<span class=\"btn btn-ghost btn-sm\" style=\"opacity:.3;cursor:default\">← Prev</span>";

        var next = result.Page < result.TotalPages
            ? $"<a href=\"{baseUrl}&page={result.Page + 1}\" class=\"btn btn-ghost btn-sm\">Next →</a>"
            : "<span class=\"btn btn-ghost btn-sm\" style=\"opacity:.3;cursor:default\">Next →</span>";

        return $"<div class=\"pagination\">{prev}{next}<span class=\"page-info\">Page {result.Page} of {result.TotalPages} ({result.TotalCount} jobs)</span></div>";
    }

    /// <summary>Renders a detail section with header and key-value grid.</summary>
    internal static string DetailSection(string sectionTitle, params (string Label, string Value)[] rows) =>
        $"<div class=\"detail-section\">" +
        $"<div class=\"detail-section-header\">{HtmlEncode(sectionTitle)}</div>" +
        $"<div class=\"detail-grid\">" +
        string.Join(string.Empty, rows.Select(r => DetailRow(r.Label, r.Value))) +
        $"</div></div>";

    /// <summary>Renders a single key-value pair in a detail grid.</summary>
    internal static string DetailRow(string label, string value) =>
        $"<div class=\"detail-label\">{HtmlEncode(label)}</div>" +
        $"<div class=\"detail-value\">{value}</div>";

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
            $"<div class=\"progress-wrap\">" +
            $"<div class=\"progress-bar-track\">" +
            $"<div id=\"progress-bar-fill\" class=\"progress-bar-fill\" style=\"width:{pct}%\"></div>" +
            $"</div>" +
            $"<div class=\"progress-info\">" +
            $"<span id=\"progress-pct\" class=\"progress-pct\">{pct}%</span>" +
            $"<span id=\"progress-msg\">{msgHtml}</span>" +
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
            ? $"<div class=\"section-title\" style=\"color:var(--danger);margin-bottom:8px;margin-top:16px\">Stack Trace</div>" +
              $"<pre style=\"border-color:rgba(248,113,113,.2);background:#0e0808\">{HtmlEncode(stackTrace)}</pre>"
            : string.Empty;

        return
            $"<div style=\"margin-bottom:24px\">" +
            $"<div class=\"section-title\" style=\"color:var(--danger);margin-bottom:8px\">Last Error</div>" +
            $"<pre style=\"border-color:rgba(248,113,113,.2);background:#0e0808\">{HtmlEncode(errorMessage)}</pre>" +
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
                $"<div class=\"section-title\" style=\"margin-bottom:8px\">Execution Logs</div>" +
                $"<p style=\"color:var(--text-3);font-size:13px\">No logs captured for this execution.</p>" +
                $"</div>";
        }

        var logLines = string.Join(string.Empty, logs.Select(entry =>
        {
            var color = entry.Level switch
            {
                "Warning" => "#fbbf24",
                "Error" or "Critical" => "#f87171",
                "Debug" or "Trace" => "#6b7280",
                _ => "#e5e7eb",
            };
            var ts = entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var msg = HtmlEncode(entry.Message).Replace("\n", "&#10;");
            return $"<span style=\"color:{color}\">[{ts}] [{entry.Level,-11}] {msg}</span>\n";
        }));

        return
            $"<div style=\"margin-bottom:24px\">" +
            $"<div class=\"section-title\" style=\"margin-bottom:8px\">Execution Logs " +
            $"<span style=\"color:var(--text-3);font-weight:400;text-transform:none;letter-spacing:0\">({logs.Count} entries)</span></div>" +
            $"<div class=\"log-terminal\">{logLines}</div>" +
            $"</div>";
    }

    /// <summary>Renders a metric card for the Overview page.</summary>
    internal static string MetricCard(string cssClass, string dotClass, string elementId, string label, long value) =>
        $"<div class=\"card {cssClass}\">" +
        $"<div class=\"card-header\"><div class=\"card-label\"><span class=\"dot {dotClass}\"></span>{HtmlEncode(label)}</div></div>" +
        $"<div id=\"{elementId}\" class=\"card-value\">{value}</div>" +
        $"</div>";

    /// <summary>Renders a queue card for the Queues page.</summary>
    internal static string QueueCard(QueueMetrics queue, string pathPrefix)
    {
        var total = queue.Enqueued + queue.Processing;
        var utilPct = total > 0 ? (int)(queue.Processing * 100.0 / total) : 0;

        return
            $"<div class=\"queue-card\">" +
            $"<div class=\"queue-card-header\">" +
            $"<div class=\"queue-name\">{HtmlEncode(queue.Queue)}</div>" +
            $"<span class=\"badge {(queue.Processing > 0 ? "badge-processing" : "badge-succeeded")}\">" +
            $"<span class=\"dot {(queue.Processing > 0 ? "dot-processing" : "dot-succeeded")}\"></span>" +
            $"{(queue.Processing > 0 ? "active" : "idle")}</span>" +
            $"</div>" +
            $"<div class=\"queue-metrics\">" +
            $"<div><div class=\"queue-metric-label\">Enqueued</div>" +
            $"<div class=\"queue-metric-val\" style=\"color:var(--info)\">{queue.Enqueued}</div></div>" +
            $"<div><div class=\"queue-metric-label\">Processing</div>" +
            $"<div class=\"queue-metric-val\" style=\"color:var(--warning)\">{queue.Processing}</div></div>" +
            $"<div><div class=\"queue-metric-label\">Total</div>" +
            $"<div class=\"queue-metric-val\">{total}</div></div>" +
            $"</div>" +
            $"<div class=\"queue-util-bar\"><div class=\"queue-util-fill\" style=\"width:{utilPct}%\"></div></div>" +
            $"<div class=\"queue-util-label\">{utilPct}% in-flight · " +
            $"<a href=\"{pathPrefix}/jobs?queue={Uri.EscapeDataString(queue.Queue)}\" style=\"font-size:11px\">View jobs →</a></div>" +
            $"</div>";
    }

    /// <summary>Renders a recurring job card for the Recurring page.</summary>
    internal static string RecurringCard(RecurringJobRecord job, string pathPrefix, DateTimeOffset now)
    {
        var effectiveCron = job.CronOverride ?? job.Cron;
        var encodedIdUrl = Uri.EscapeDataString(job.RecurringJobId);

        // Countdown / next execution
        string nextHtml;
        if (job.DeletedByUser)
        {
            nextHtml = "<span style=\"color:var(--text-3)\">deleted</span>";
        }
        else if (!job.Enabled)
        {
            nextHtml = "<span style=\"color:var(--warning)\">paused</span>";
        }
        else if (job.NextExecution.HasValue)
        {
            nextHtml = $"<span style=\"color:var(--accent-light)\">{Helpers.CountdownFriendly(job.NextExecution.Value - now)}</span>";
        }
        else
        {
            nextHtml = "<span style=\"color:var(--text-3)\">—</span>";
        }

        // Last execution status badge
        string lastRunHtml;
        if (job.LastExecutedAt.HasValue)
        {
            var statusColor = job.LastExecutionStatus switch
            {
                JobStatus.Succeeded => "var(--success)",
                JobStatus.Failed => "var(--danger)",
                _ => "var(--text-3)",
            };
            var statusIcon = job.LastExecutionStatus == JobStatus.Failed ? "✗" : "✓";
            var title = job.LastExecutionStatus == JobStatus.Failed && job.LastExecutionError is not null
                ? $" title=\"{HttpUtility.HtmlAttributeEncode(job.LastExecutionError)}\""
                : string.Empty;
            lastRunHtml = $"<span style=\"color:{statusColor};font-size:12px\"{title}>{statusIcon} {Helpers.RelativeTime(job.LastExecutedAt, now)}</span>";
        }
        else
        {
            lastRunHtml = "<span style=\"color:var(--text-3);font-size:12px\">never run</span>";
        }

        // State badges
        var stateBadges = string.Empty;
        if (job.DeletedByUser)
        {
            stateBadges += " <span class=\"badge badge-deleted\">Deleted</span>";
        }
        else if (!job.Enabled)
        {
            stateBadges += " <span class=\"badge badge-processing\">Paused</span>";
        }

        if (job.ConcurrencyPolicy == RecurringConcurrencyPolicy.AllowConcurrent)
        {
            stateBadges += " <span class=\"badge badge-scheduled\">⟳ concurrent</span>";
        }

        if (job.CronOverride is not null)
        {
            stateBadges += $" <span class=\"badge badge-awaiting\" title=\"Default: {HttpUtility.HtmlAttributeEncode(job.Cron)}\">cron overridden</span>";
        }

        // Action buttons
        string actionsHtml;
        if (job.DeletedByUser)
        {
            actionsHtml =
                $"<form method=\"post\" action=\"{pathPrefix}/recurring/{encodedIdUrl}/restore\" style=\"display:inline\">" +
                "<button type=\"submit\" class=\"btn btn-ghost btn-sm\">↩ Restore</button></form>";
        }
        else
        {
            var pauseResume = job.Enabled
                ? $"<form method=\"post\" action=\"{pathPrefix}/recurring/{encodedIdUrl}/pause\" style=\"display:inline\"><button type=\"submit\" class=\"btn btn-ghost btn-sm\">⏸ Pause</button></form>"
                : $"<form method=\"post\" action=\"{pathPrefix}/recurring/{encodedIdUrl}/resume\" style=\"display:inline\"><button type=\"submit\" class=\"btn btn-primary btn-sm\">▶ Resume</button></form>";

            actionsHtml =
                $"<form method=\"post\" action=\"{pathPrefix}/recurring/{encodedIdUrl}/trigger\" style=\"display:inline\">" +
                "<button type=\"submit\" class=\"btn btn-primary btn-sm\">▶ Trigger</button></form> " +
                pauseResume + " " +
                $"<a href=\"{pathPrefix}/recurring/{encodedIdUrl}\" class=\"btn btn-ghost btn-sm\">Details</a>";
        }

        return
            $"<div class=\"recurring-card\">" +
            $"<div class=\"recurring-card-header\">" +
            $"<div class=\"recurring-card-left\">" +
            $"<span class=\"recurring-id\">{HtmlEncode(job.RecurringJobId)}</span>" +
            stateBadges +
            $"</div>" +
            $"<div class=\"recurring-card-right\">" +
            lastRunHtml +
            $"</div>" +
            $"</div>" +
            $"<div class=\"recurring-card-meta\">" +
            $"<span>{HtmlEncode(Helpers.ShortType(job.JobType))}</span>" +
            $"<code class=\"cron\">{HtmlEncode(effectiveCron)}</code>" +
            $"<span>{HtmlEncode(job.Queue)}</span>" +
            $"<span>Next: {nextHtml}</span>" +
            $"</div>" +
            $"<div style=\"margin-top:10px;display:flex;gap:6px;flex-wrap:wrap\">{actionsHtml}</div>" +
            $"</div>";
    }

    /// <summary>Renders a server table row.</summary>
    internal static string ServerRow(ServerRecord server, DateTimeOffset now)
    {
        var uptime = server.HeartbeatAt - server.StartedAt;
        var heartbeatAge = now - server.HeartbeatAt;
        var heartbeatStatus = heartbeatAge switch
        {
            var d when d.TotalSeconds < 60 => $"<span class=\"dot dot-processing\"></span> Active",
            var d when d.TotalMinutes < 5 => $"<span class=\"dot dot-scheduled\"></span> Recent",
            var d when d.TotalHours < 24 => $"<span class=\"dot dot-enqueued\"></span> Stale",
            _ => $"<span class=\"dot dot-failed\"></span> Offline",
        };

        return
            $"<tr>" +
            $"<td style=\"font-family:monospace;font-size:11px;color:var(--text-3)\">{server.Id}</td>" +
            $"<td>{Helpers.RelativeTime(server.StartedAt, now)}</td>" +
            $"<td>{uptime.TotalDays:F1}d {uptime.Hours}h</td>" +
            $"<td><span style=\"color:var(--info)\">{server.WorkerCount}</span></td>" +
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
            ? $"<a href=\"{pathPrefix}/recurring/{encodedJobId}?page={result.Page - 1}&pageSize={pageSize}\" class=\"btn btn-sm\">← Prev</a>"
            : string.Empty;

        var next = result.Page < totalPages
            ? $"<a href=\"{pathPrefix}/recurring/{encodedJobId}?page={result.Page + 1}&pageSize={pageSize}\" class=\"btn btn-sm\">Next →</a>"
            : string.Empty;

        return
            $"<div class=\"pagination\">" +
            prev +
            $"<span class=\"page-info\">Page {result.Page} of {totalPages} ({result.TotalCount} total)</span>" +
            next +
            "</div>";
    }

    /// <summary>Returns the read-only mode warning banner HTML.</summary>
    internal static string ReadOnlyBanner() => ReadOnlyBannerHtml;

    /// <summary>Renders a visual execution flow timeline.</summary>
    internal static string ExecutionTimeline(JobRecord job, DateTimeOffset now)
    {
        var events = BuildTimelineEvents(job, now).ToList();

        var sb = new System.Text.StringBuilder();
        sb.Append("<div class=\"timeline\">");

        for (int i = 0; i < events.Count; i++)
        {
            var @event = events[i];
            var isLast = i == events.Count - 1;
            var nodeClass = GetTimelineNodeClass(@event.CssClass, isLast);
            var timeStr = @event.At.HasValue ? $"{@event.At.Value:HH:mm:ss}" : "—";

            sb.Append($"<div class=\"timeline-item\">");
            sb.Append($"<div class=\"timeline-node {nodeClass}\" data-status=\"{@event.CssClass}\"></div>");
            sb.Append($"<div class=\"timeline-content\">");
            sb.Append($"<div class=\"timeline-label\">{HtmlEncode(@event.Label)}</div>");

            if (!string.IsNullOrEmpty(@event.Subtitle))
            {
                sb.Append($"<div class=\"timeline-metadata\">{HtmlEncode(@event.Subtitle)}</div>");
            }

            sb.Append($"<div class=\"timeline-time\">{timeStr}</div>");

            if (!string.IsNullOrEmpty(@event.Error))
            {
                sb.Append($"<div class=\"timeline-error\">{HtmlEncode(@event.Error)}</div>");
            }

            sb.Append($"</div>");
            sb.Append($"</div>");

            if (!isLast)
            {
                sb.Append($"<div class=\"timeline-line\"></div>");
            }
        }

        sb.Append("</div>");

        return sb.ToString();
    }

    /// <summary>Builds detailed timeline events from job record.</summary>
    private static IEnumerable<TimelineEvent> BuildTimelineEvents(JobRecord job, DateTimeOffset now)
    {
        // Enqueued — initial state
        yield return new TimelineEvent(
            job.CreatedAt,
            "Enqueued",
            "enqueued",
            $"queue: {HtmlEncode(job.Queue)} · priority: {job.Priority}",
            null);

        // Processing attempts — simulate each attempt
        if (job.ProcessingStartedAt.HasValue)
        {
            // For the first attempt, show as Processing
            yield return new TimelineEvent(
                job.ProcessingStartedAt.Value,
                "Processing",
                "processing",
                $"attempt 1/{job.MaxAttempts}",
                null);

            // If there were retries (attempts > 1), show them as Processing with retry marker
            if (job.Attempts > 1)
            {
                for (int i = 2; i <= job.Attempts; i++)
                {
                    // Retries happened — show as Failed then retry scheduled
                    yield return new TimelineEvent(
                        job.CompletedAt,
                        "Failed",
                        "failed",
                        null,
                        job.LastErrorMessage);

                    // Show retry scheduled
                    if (job.RetryAt.HasValue && i == job.Attempts)
                    {
                        yield return new TimelineEvent(
                            job.RetryAt.Value,
                            "Retry scheduled",
                            "scheduled",
                            Helpers.CountdownFriendly(job.RetryAt.Value - now),
                            null);

                        // Show next processing attempt
                        if (job.RetryAt.Value <= now)
                        {
                            yield return new TimelineEvent(
                                job.RetryAt.Value,
                                "Processing",
                                "processing",
                                $"attempt {i}/{job.MaxAttempts}",
                                null);
                        }
                    }
                }
            }
        }

        // Terminal states
        if (job.Status == JobStatus.Succeeded && job.CompletedAt.HasValue)
        {
            yield return new TimelineEvent(
                job.CompletedAt.Value,
                "Succeeded",
                "succeeded",
                null,
                null);
        }
        else if (job.Status == JobStatus.Failed && job.CompletedAt.HasValue)
        {
            // Final failure (no more retries)
            if (!job.RetryAt.HasValue || job.RetryAt.Value <= now)
            {
                yield return new TimelineEvent(
                    job.CompletedAt.Value,
                    "Failed",
                    "failed",
                    null,
                    job.LastErrorMessage);

                // Dead-letter if applicable
                yield return new TimelineEvent(
                    job.CompletedAt.Value,
                    "Dead-letter",
                    "dead",
                    "handler invoked",
                    null);
            }
        }
        else if (job.Status == JobStatus.Expired && job.ExpiresAt.HasValue)
        {
            yield return new TimelineEvent(
                job.ExpiresAt.Value,
                "Expired",
                "expired",
                "deadline passed before execution",
                null);
        }
    }

    private static string GetTimelineNodeClass(string cssClass, bool isFinal) =>
        cssClass switch
        {
            "succeeded" => isFinal ? "timeline-node-success timeline-node-final" : "timeline-node-success",
            "failed" or "dead" => isFinal ? "timeline-node-error timeline-node-final" : "timeline-node-error",
            "expired" => isFinal ? "timeline-node-muted timeline-node-final" : "timeline-node-muted",
            "processing" => "timeline-node-active",
            "scheduled" => isFinal ? "timeline-node-warning timeline-node-final" : "timeline-node-warning",
            "enqueued" => "timeline-node-neutral",
            _ => "timeline-node-neutral",
        };

    private static string HtmlEncode(string? text) => HttpUtility.HtmlEncode(text ?? string.Empty);

    /// <summary>Timeline event record.</summary>
    private sealed record TimelineEvent(
        DateTimeOffset? At,
        string Label,
        string CssClass,
        string? Subtitle,
        string? Error);
}
