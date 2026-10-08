using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace NexJob.Dashboard.Pages;

[ExcludeFromCodeCoverage]
internal sealed class RecurringJobDetailPage : IComponent
{
    private RenderHandle _handle;

    /// <summary>Gets or sets the recurring job definition to display.</summary>
    [Parameter] public RecurringJobRecord Job { get; set; } = default!;

    /// <summary>Gets or sets the paginated list of job execution records for this recurring job.</summary>
    [Parameter] public PagedResult<JobRecord> Executions { get; set; } = default!;

    /// <summary>Gets or sets the number of executions shown per page.</summary>
    [Parameter] public int PageSize { get; set; } = 20;

    /// <summary>Gets or sets the dashboard path prefix.</summary>
    [Parameter] public string PathPrefix { get; set; } = "/dashboard";

    /// <summary>Gets or sets the page title.</summary>
    [Parameter] public string Title { get; set; } = "NexJob";

    /// <summary>Gets or sets shared navigation counters.</summary>
    [Parameter] public NavCounters? Counters { get; set; }

    /// <summary>Gets or sets optional federated clusters list.</summary>
    [Parameter] public IReadOnlyList<DashboardCluster>? Clusters { get; set; }

    /// <summary>Gets or sets currently active cluster.</summary>
    [Parameter] public DashboardCluster? ActiveCluster { get; set; }

    /// <summary>Gets or sets the set of queue names that currently have at least one active worker listening.</summary>
    [Parameter] public IReadOnlySet<string>? ActiveWorkerQueues { get; set; }

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml()));
        return Task.CompletedTask;
    }

    private string BuildHtml()
    {
        var now = DateTimeOffset.UtcNow;
        var job = Job;

        var encodedId = System.Web.HttpUtility.HtmlEncode(job.RecurringJobId);
        var encodedIdUrl = Uri.EscapeDataString(job.RecurringJobId);
        var effectiveCron = job.CronOverride ?? job.Cron;
        var cronDesc = Helpers.DescribeCron(effectiveCron);

        // ── State badge & last execution indicator ────────────────────────────
        string stateBadge;
        if (job.DeletedByUser)
        {
            stateBadge = "<span class=\"badge badge-error\">Deleted</span>";
        }
        else if (!job.Enabled)
        {
            stateBadge = "<span class=\"badge badge-warning\">Paused</span>";
        }
        else
        {
            stateBadge = "<span class=\"badge badge-success\">Active</span>";
        }

        var lastExecBadge = job.LastExecutionStatus switch
        {
            JobStatus.Succeeded => "<span class=\"badge badge-success\" style=\"font-size:11px\">✓ Last Run Succeeded</span>",
            JobStatus.Failed => $"<span class=\"badge badge-error\" style=\"font-size:11px\" title=\"{System.Web.HttpUtility.HtmlAttributeEncode(job.LastExecutionError ?? string.Empty)}\">✗ Last Run Failed</span>",
            _ => string.Empty,
        };

        // ── Action buttons ────────────────────────────────────────────────────
        var clusterSuffix = ActiveCluster is not null ? $"?cluster={Uri.EscapeDataString(ActiveCluster.Id)}" : string.Empty;
        var clusterParam = ActiveCluster is not null ? $"&cluster={Uri.EscapeDataString(ActiveCluster.Id)}" : string.Empty;
        var isReadOnly = ActiveCluster?.IsReadOnly == true;

        string actionsHtml;
        if (isReadOnly)
        {
            actionsHtml = string.Empty;
        }
        else if (job.DeletedByUser)
        {
            actionsHtml =
                $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/restore{clusterSuffix}\" style=\"display:inline\">" +
                "<button type=\"submit\" class=\"btn btn-primary btn-sm\">↩ Restore Schedule</button></form>";
        }
        else
        {
            var queueOrphanWarning = ActiveWorkerQueues is { Count: > 0 } && !ActiveWorkerQueues.Contains(job.Queue, StringComparer.OrdinalIgnoreCase)
                ? $"\\n\\n⚠ Warning: Queue '{job.Queue}' has no active workers. The job will remain queued until a worker starts."
                : string.Empty;
            var triggerConfirm = System.Web.HttpUtility.JavaScriptStringEncode($"Trigger '{job.RecurringJobId}' now?{queueOrphanWarning}", addDoubleQuotes: true);

            var triggerButton =
                $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/trigger{clusterSuffix}\" style=\"display:inline\">" +
                $"<button type=\"submit\" class=\"btn btn-primary btn-sm\" onclick=\"return confirm({triggerConfirm})\">" +
                $"<svg width=\"14\" height=\"14\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><path d=\"M13 2L3 14h9l-1 8 10-12h-9l1-8z\"/></svg> Trigger Now</button></form>";

            var pauseResumeButton = job.Enabled
                ? $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/pause{clusterSuffix}\" style=\"display:inline\">" +
                  $"<button type=\"submit\" class=\"btn btn-secondary btn-sm\" style=\"color:var(--warning)\">" +
                  $"<svg width=\"14\" height=\"14\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><rect x=\"6\" y=\"4\" width=\"4\" height=\"16\"/><rect x=\"14\" y=\"4\" width=\"4\" height=\"16\"/></svg> Pause</button></form>"
                : $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/resume{clusterSuffix}\" style=\"display:inline\">" +
                  $"<button type=\"submit\" class=\"btn btn-secondary btn-sm\" style=\"color:var(--success)\">" +
                  $"<svg width=\"14\" height=\"14\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><polygon points=\"5 3 19 12 5 21 5 3\"/></svg> Resume</button></form>";

            var editScheduleButton =
                $"<button type=\"button\" class=\"btn btn-secondary btn-sm\" onclick=\"toggleEditSchedule()\">" +
                $"<svg width=\"14\" height=\"14\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><path d=\"M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7\"/><path d=\"M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z\"/></svg> Edit Schedule</button>";

            var forceDeleteButton =
                $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/force-delete{clusterSuffix}\" style=\"display:inline\">" +
                "<button type=\"submit\" class=\"btn btn-danger btn-sm\" onclick=\"return confirm('Delete this recurring job schedule?')\">" +
                "<svg width=\"14\" height=\"14\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><polyline points=\"3 6 5 6 21 6\"/><path d=\"M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2\"/></svg> Delete</button></form>";

            actionsHtml =
                $"<div style=\"display:flex;gap:8px;flex-wrap:wrap;align-items:center\">" +
                triggerButton +
                pauseResumeButton +
                editScheduleButton +
                forceDeleteButton +
                "</div>";
        }

        // ── Inline Edit Schedule Form ─────────────────────────────────────────
        var resetBtn = job.CronOverride is not null
            ? $"<button type=\"submit\" name=\"cronOverride\" value=\"\" class=\"btn btn-secondary btn-sm\">Reset to Default</button>"
            : string.Empty;

        var editPanel =
            $"<div id=\"edit-schedule-panel\" class=\"card\" style=\"display:none;margin-bottom:24px;border:1px solid var(--primary)\">" +
            $"<div class=\"card-header\"><h3 style=\"margin:0;font-size:14px;font-weight:600\">Modify Schedule Expression</h3></div>" +
            $"<div style=\"padding:16px 20px\">" +
            $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/update-config{clusterSuffix}\" style=\"display:flex;gap:12px;align-items:center;flex-wrap:wrap\">" +
            $"<div style=\"display:flex;flex-direction:column;gap:4px\">" +
            $"<label style=\"font-size:11px;font-weight:600;color:var(--text-secondary);text-transform:uppercase\">Cron Expression</label>" +
            $"<input type=\"text\" name=\"cronOverride\" placeholder=\"{System.Web.HttpUtility.HtmlAttributeEncode(effectiveCron)}\" value=\"{System.Web.HttpUtility.HtmlAttributeEncode(job.CronOverride ?? string.Empty)}\" style=\"font-family:monospace;width:240px;font-size:13px\" />" +
            $"</div>" +
            $"<div style=\"display:flex;gap:8px;align-items:flex-end;margin-top:auto\">" +
            $"<button type=\"submit\" class=\"btn btn-primary btn-sm\">Save Schedule</button>" +
            resetBtn +
            $"<button type=\"button\" class=\"btn btn-secondary btn-sm\" onclick=\"toggleEditSchedule()\">Cancel</button>" +
            $"</div>" +
            $"</form>" +
            $"<div style=\"font-size:11px;color:var(--text-tertiary);margin-top:8px\">Default configuration: <code>{System.Web.HttpUtility.HtmlEncode(job.Cron)}</code>. Supports standard 5-field and 6-field (with seconds).</div>" +
            $"</div></div>";

        // ── Overview Cards (Schedule, Queue, Health) ──────────────────────────
        var cronOverrideTag = job.CronOverride is not null
            ? "<span class=\"badge badge-warning\" style=\"font-size:10px;margin-left:6px\" title=\"Overridden by operator\">custom</span>"
            : string.Empty;

        var cronDescHtml = cronDesc is not null
            ? $"<div style=\"font-size:12px;color:var(--text-secondary);margin-top:4px\">{System.Web.HttpUtility.HtmlEncode(cronDesc)}</div>"
            : string.Empty;

        string nextCountdown;
        if (!job.NextExecution.HasValue || !job.Enabled || job.DeletedByUser)
        {
            nextCountdown = "—";
        }
        else if (job.NextExecution.Value <= now)
        {
            nextCountdown = "Due now";
        }
        else
        {
            nextCountdown = $"in {Helpers.FormatCountdown(job.NextExecution.Value - now)}";
        }

        var nextExactUtc = job.NextExecution.HasValue && job.Enabled && !job.DeletedByUser
            ? $"{job.NextExecution.Value:yyyy-MM-dd HH:mm:ss} UTC"
            : string.Empty;

        var hasWorkers = ActiveWorkerQueues is null || ActiveWorkerQueues.Contains(job.Queue, StringComparer.OrdinalIgnoreCase);
        var workerBadge = hasWorkers
            ? "<span style=\"color:var(--success);font-size:11px;display:inline-flex;align-items:center;gap:4px\"><span class=\"dot dot-succeeded\" style=\"width:6px;height:6px\"></span> Active workers listening</span>"
            : "<span style=\"color:var(--warning);font-size:11px;display:inline-flex;align-items:center;gap:4px\"><span class=\"dot dot-processing\" style=\"width:6px;height:6px\"></span> ⚠ No active workers on queue</span>";

        var concurrencyHint = job.ConcurrencyPolicy == RecurringConcurrencyPolicy.SkipIfRunning
            ? "Skips execution if previous run is still active"
            : "Allows parallel executions across triggers";

        var lastStatusDot = job.LastExecutionStatus switch
        {
            JobStatus.Succeeded => "<span class=\"dot dot-succeeded\" style=\"width:8px;height:8px\"></span>",
            JobStatus.Failed => "<span class=\"dot dot-failed\" style=\"width:8px;height:8px\"></span>",
            _ => "<span class=\"dot dot-default\" style=\"width:8px;height:8px\"></span>",
        };

        var lastStatusText = job.LastExecutionStatus?.ToString() ?? (job.LastExecutedAt.HasValue ? "Completed" : "Never run");
        var lastRelative = job.LastExecutedAt.HasValue ? Helpers.RelativeTime(job.LastExecutedAt, now) : "no executions";
        var lastExactUtc = job.LastExecutedAt.HasValue ? $"{job.LastExecutedAt.Value:yyyy-MM-dd HH:mm:ss} UTC" : string.Empty;

        var overviewCards =
            $"<div style=\"display:grid;grid-template-columns:repeat(auto-fit, minmax(min(280px, 100%), 1fr));gap:20px;margin-bottom:28px\">" +
            // Card 1: Schedule
            $"<div class=\"card\" style=\"margin-bottom:0\">" +
            $"<div class=\"card-header\"><h3 style=\"margin:0;font-size:14px;font-weight:600\">Schedule & Frequency</h3></div>" +
            $"<div style=\"padding:16px 20px;display:flex;flex-direction:column;gap:14px\">" +
            $"<div>" +
            $"<div style=\"font-size:11px;color:var(--text-tertiary);text-transform:uppercase;font-weight:600;margin-bottom:6px\">Cron Expression</div>" +
            $"<div style=\"display:flex;align-items:center\">" +
            $"<code style=\"background:var(--bg-tertiary);color:var(--primary);padding:4px 8px;border-radius:4px;font-size:13px;border:1px solid var(--border);font-family:monospace\">{System.Web.HttpUtility.HtmlEncode(effectiveCron)}</code>" +
            cronOverrideTag +
            $"</div>" +
            cronDescHtml +
            $"</div>" +
            $"<div style=\"border-top:1px solid var(--border);padding-top:10px\">" +
            $"<div style=\"font-size:11px;color:var(--text-tertiary);text-transform:uppercase;font-weight:600;margin-bottom:4px\">Next Occurrence</div>" +
            $"<div style=\"font-size:14px;font-weight:600;color:var(--text-primary)\">{nextCountdown}</div>" +
            (string.IsNullOrEmpty(nextExactUtc) ? string.Empty : $"<div style=\"font-size:11px;color:var(--text-tertiary)\">{nextExactUtc}</div>") +
            $"</div>" +
            $"</div></div>" +
            // Card 2: Queue & Routing
            $"<div class=\"card\" style=\"margin-bottom:0\">" +
            $"<div class=\"card-header\"><h3 style=\"margin:0;font-size:14px;font-weight:600\">Queue & Routing</h3></div>" +
            $"<div style=\"padding:16px 20px;display:flex;flex-direction:column;gap:14px\">" +
            $"<div>" +
            $"<div style=\"font-size:11px;color:var(--text-tertiary);text-transform:uppercase;font-weight:600;margin-bottom:6px\">Destination Queue</div>" +
            $"<div style=\"display:flex;align-items:center;gap:10px;margin-bottom:4px\">" +
            $"<span style=\"background:var(--bg-tertiary);color:var(--text-primary);padding:3px 8px;border-radius:12px;font-size:12px;border:1px solid var(--border);font-family:monospace\">{System.Web.HttpUtility.HtmlEncode(job.Queue)}</span>" +
            $"</div>" +
            workerBadge +
            $"</div>" +
            $"<div style=\"border-top:1px solid var(--border);padding-top:10px\">" +
            $"<div style=\"font-size:11px;color:var(--text-tertiary);text-transform:uppercase;font-weight:600;margin-bottom:4px\">Concurrency Policy</div>" +
            $"<div style=\"font-size:13px;color:var(--text-primary);font-weight:600\">{job.ConcurrencyPolicy}</div>" +
            $"<div style=\"font-size:11px;color:var(--text-tertiary)\">{concurrencyHint}</div>" +
            $"</div>" +
            $"</div></div>" +
            // Card 3: Health & Last Run
            $"<div class=\"card\" style=\"margin-bottom:0\">" +
            $"<div class=\"card-header\"><h3 style=\"margin:0;font-size:14px;font-weight:600\">Health & Last Run</h3></div>" +
            $"<div style=\"padding:16px 20px;display:flex;flex-direction:column;gap:14px\">" +
            $"<div>" +
            $"<div style=\"font-size:11px;color:var(--text-tertiary);text-transform:uppercase;font-weight:600;margin-bottom:6px\">Latest Status</div>" +
            $"<div style=\"display:flex;align-items:center;gap:8px\">" +
            lastStatusDot +
            $"<span style=\"font-size:13px;font-weight:600;color:var(--text-primary)\">{lastStatusText}</span>" +
            $"<span style=\"font-size:11px;color:var(--text-tertiary)\">({lastRelative})</span>" +
            $"</div>" +
            (string.IsNullOrEmpty(lastExactUtc) ? string.Empty : $"<div style=\"font-size:11px;color:var(--text-tertiary);margin-top:2px\">{lastExactUtc}</div>") +
            $"</div>" +
            $"<div style=\"border-top:1px solid var(--border);padding-top:10px\">" +
            $"<div style=\"font-size:11px;color:var(--text-tertiary);text-transform:uppercase;font-weight:600;margin-bottom:4px\">Job Implementation</div>" +
            $"<div style=\"font-size:12px;color:var(--text-primary);font-family:monospace;white-space:nowrap;overflow:hidden;text-overflow:ellipsis\" title=\"{System.Web.HttpUtility.HtmlAttributeEncode(job.JobType)}\">{System.Web.HttpUtility.HtmlEncode(Helpers.ShortType(job.JobType))}</div>" +
            $"<div style=\"font-size:11px;color:var(--text-tertiary)\">Created {job.CreatedAt:yyyy-MM-dd}</div>" +
            $"</div>" +
            $"</div></div>" +
            $"</div>";

        // ── Error Alert (if last run failed) ──────────────────────────────────
        var errorBanner = string.Empty;
        if (!string.IsNullOrWhiteSpace(job.LastExecutionError))
        {
            errorBanner =
                $"<div class=\"card\" style=\"background:var(--error-light);border:1px solid rgba(234, 84, 85, 0.35);margin-bottom:24px;padding:16px 20px\">" +
                $"<div style=\"display:flex;align-items:center;gap:8px;color:var(--error);font-weight:600;font-size:13px;margin-bottom:6px\">" +
                $"<svg width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><circle cx=\"12\" cy=\"12\" r=\"10\"/><line x1=\"12\" y1=\"8\" x2=\"12\" y2=\"12\"/><line x1=\"12\" y1=\"16\" x2=\"12.01\" y2=\"16\"/></svg>" +
                $"Last Execution Encountered a Fault" +
                $"</div>" +
                $"<pre style=\"background:transparent;color:var(--text-primary);font-family:monospace;font-size:12px;line-height:1.5;margin:0;white-space:pre-wrap;word-break:break-all\">{System.Web.HttpUtility.HtmlEncode(job.LastExecutionError)}</pre>" +
                $"</div>";
        }

        // ── Executions table ──────────────────────────────────────────────────
        string executionsSection;
        if (Executions.Items.Count == 0)
        {
            executionsSection =
                "<div class=\"card\">" +
                "<div class=\"card-header\"><h3 style=\"margin:0;font-size:15px;font-weight:600\">Execution History</h3></div>" +
                "<div style=\"padding:48px 24px;text-align:center;color:var(--text-tertiary)\">" +
                $"<svg width=\"44\" height=\"44\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1\" style=\"margin-bottom:12px;opacity:0.7\"><path d=\"{HtmlFragments.EmptyIconInbox}\"/></svg>" +
                "<p style=\"font-size:14px;margin:0\">No execution history recorded for this schedule yet.</p>" +
                "<p style=\"font-size:12px;color:var(--text-tertiary);margin-top:6px\">Runs will appear here once the schedule fires or when triggered manually.</p>" +
                "</div>" +
                "</div>";
        }
        else
        {
            var rows = string.Join(string.Empty, Executions.Items.Select((exec, idx) =>
            {
                var rowNum = ((Executions.Page - 1) * Executions.PageSize) + idx + 1;
                var duration = exec.ProcessingStartedAt.HasValue && exec.CompletedAt.HasValue
                    ? $"{(exec.CompletedAt.Value - exec.ProcessingStartedAt.Value).TotalSeconds:F1}s"
                    : "<span style=\"color:var(--text-tertiary)\">—</span>";

                var startedRelative = exec.ProcessingStartedAt.HasValue
                    ? Helpers.RelativeTime(exec.ProcessingStartedAt, now)
                    : "<span style=\"color:var(--text-tertiary)\">—</span>";

                var startedExact = exec.ProcessingStartedAt.HasValue
                    ? $"{exec.ProcessingStartedAt.Value:yyyy-MM-dd HH:mm:ss} UTC"
                    : string.Empty;

                var idStr = exec.Id.Value.ToString();
                var shortId = idStr[..Math.Min(8, idStr.Length)];

                return
                    $"<tr>" +
                    $"<td style=\"color:var(--text-tertiary);font-size:12px\">{rowNum}</td>" +
                    $"<td>{Helpers.BadgeHtml(exec.Status)}</td>" +
                    $"<td><a href=\"{PathPrefix}/jobs/{idStr}{clusterSuffix}\" style=\"color:var(--primary);font-family:monospace;font-size:12px;text-decoration:none;font-weight:500\">{shortId}…</a></td>" +
                    $"<td><div style=\"font-size:12px;color:var(--text-primary)\">{startedRelative}</div>" +
                    (string.IsNullOrEmpty(startedExact) ? string.Empty : $"<div style=\"font-size:10px;color:var(--text-tertiary)\">{startedExact}</div>") +
                    $"</td>" +
                    $"<td style=\"font-size:12px;color:var(--text-secondary);font-family:monospace\">{duration}</td>" +
                    $"<td style=\"font-size:12px;color:var(--text-secondary)\">{exec.Attempts} / {exec.MaxAttempts}</td>" +
                    $"<td style=\"text-align:right\"><button type=\"button\" onclick=\"showLogs('{idStr}')\" class=\"btn btn-secondary btn-sm\" style=\"gap:4px\">" +
                    $"<svg width=\"12\" height=\"12\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><polyline points=\"4 17 10 11 4 5\"/><line x1=\"12\" y1=\"19\" x2=\"20\" y2=\"19\"/></svg> View Logs</button></td>" +
                    $"</tr>";
            }));

            var opt10 = PageSize == 10 ? " selected" : string.Empty;
            var opt20 = PageSize == 20 ? " selected" : string.Empty;
            var opt50 = PageSize == 50 ? " selected" : string.Empty;

            var clusterHiddenInput = ActiveCluster is not null
                ? $"<input type=\"hidden\" name=\"cluster\" value=\"{System.Web.HttpUtility.HtmlAttributeEncode(ActiveCluster.Id)}\" />"
                : string.Empty;

            var pageSizeSelector =
                $"<form method=\"get\" action=\"{PathPrefix}/recurring/{encodedIdUrl}\" style=\"display:inline-flex;align-items:center;gap:8px\">" +
                clusterHiddenInput +
                "<label style=\"color:var(--text-secondary);font-size:12px\">Show:</label>" +
                "<select name=\"pageSize\" onchange=\"this.form.submit()\" style=\"padding:4px 8px;font-size:12px\">" +
                $"<option value=\"10\"{opt10}>10</option>" +
                $"<option value=\"20\"{opt20}>20</option>" +
                $"<option value=\"50\"{opt50}>50</option>" +
                "</select>" +
                "<input type=\"hidden\" name=\"page\" value=\"1\" />" +
                "<label style=\"color:var(--text-secondary);font-size:12px\">per page</label>" +
                "</form>";

            var tableHtml =
                "<div class=\"table-container\"><table class=\"table\"><thead><tr>" +
                "<th style=\"width:40px\">#</th><th>Status</th><th>Job ID</th><th>Started</th><th>Duration</th><th>Attempts</th><th style=\"text-align:right\">Logs</th>" +
                $"</tr></thead><tbody>{rows}</tbody></table></div>";

            var paginationHtml = BuildPagination(Executions, encodedIdUrl, clusterParam);

            executionsSection =
                "<div class=\"card\">" +
                "<div class=\"card-header\" style=\"display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:12px\">" +
                $"<div style=\"display:flex;align-items:center;gap:12px\"><h3 style=\"margin:0;font-size:15px;font-weight:600\">Execution History</h3><span style=\"font-size:12px;color:var(--text-tertiary);background:var(--bg-tertiary);padding:2px 8px;border-radius:12px;border:1px solid var(--border)\">{Executions.TotalCount} total runs</span></div>" +
                pageSizeSelector +
                "</div>" +
                tableHtml +
                paginationHtml +
                "</div>";
        }

        // ── Theme-Safe Modal Dialog & Script ──────────────────────────────────
        var modalHtml =
            "<style>#logModal{position:fixed;top:50%;left:50%;transform:translate(-50%,-50%);margin:0}#logModal::backdrop{background:rgba(0,0,0,.65)}</style>" +
            "<dialog id=\"logModal\" style=\"background:var(--bg-primary);color:var(--text-primary);border:1px solid var(--border);border-radius:var(--radius);padding:0;max-width:850px;width:92vw;max-height:85vh;box-shadow:var(--shadow)\">" +
            "<div style=\"display:flex;justify-content:space-between;align-items:center;padding:16px 20px;border-bottom:1px solid var(--border)\">" +
            "<h3 id=\"logModalTitle\" style=\"margin:0;font-size:15px;font-weight:600;color:var(--text-primary)\">Execution Logs</h3>" +
            "<div style=\"display:flex;align-items:center;gap:10px\">" +
            "<button type=\"button\" class=\"copy-btn\" onclick=\"nexJobCopyCode(this)\" title=\"Copy execution logs\">📋 Copy Logs</button>" +
            "<button type=\"button\" onclick=\"document.getElementById('logModal').close()\" style=\"background:none;border:none;color:var(--text-secondary);cursor:pointer;font-size:22px;line-height:1\">&times;</button>" +
            "</div>" +
            "</div>" +
            "<div style=\"padding:16px 20px;overflow-y:auto;max-height:calc(85vh - 65px)\">" +
            "<pre id=\"logModalContent\" style=\"background:var(--bg-tertiary);color:var(--text-primary);border:1px solid var(--border);border-radius:6px;padding:14px 18px;font-family:monospace;font-size:12px;line-height:1.6;white-space:pre-wrap;word-break:break-all;margin:0\"></pre>" +
            "</div>" +
            "</dialog>" +
            $"<script>\n" +
            "function toggleEditSchedule() {\n" +
            "  const p = document.getElementById('edit-schedule-panel');\n" +
            "  if (p) { p.style.display = p.style.display === 'none' ? 'block' : 'none'; }\n" +
            "}\n" +
            "async function showLogs(jobId) {\n" +
            "  const modal = document.getElementById('logModal');\n" +
            "  const content = document.getElementById('logModalContent');\n" +
            "  const title = document.getElementById('logModalTitle');\n" +
            "  content.textContent = 'Loading execution logs...';\n" +
            "  title.textContent = 'Execution Logs \\u2014 ' + jobId.substring(0, 8) + '...';\n" +
            "  modal.showModal();\n" +
            "  try {\n" +
            $"    const res = await fetch(`{PathPrefix}/jobs/${{jobId}}/logs{clusterSuffix}`);\n" +
            "    const logs = await res.json();\n" +
            "    if (!logs || logs.length === 0) {\n" +
            "      content.textContent = 'No logs captured for this execution.';\n" +
            "      return;\n" +
            "    }\n" +
            "    const levelColors = {\n" +
            "      'Trace': 'var(--text-tertiary)', 'Debug': 'var(--text-tertiary)', 'Information': 'var(--text-primary)',\n" +
            "      'Warning': 'var(--warning)', 'Error': 'var(--error)', 'Critical': 'var(--error)'\n" +
            "    };\n" +
            "    content.innerHTML = '';\n" +
            "    for (const log of logs) {\n" +
            "      const color = levelColors[log.level] || 'var(--text-primary)';\n" +
            "      const levelPad = (log.level || 'Info').padEnd(11);\n" +
            "      const span = document.createElement('span');\n" +
            "      span.style.color = color;\n" +
            "      span.textContent = `[${log.timestamp}] [${levelPad}] ${log.message}\\n`;\n" +
            "      content.appendChild(span);\n" +
            "    }\n" +
            "  } catch (e) {\n" +
            "    content.textContent = 'Failed to load logs.';\n" +
            "  }\n" +
            "}\n" +
            "let isDraggingModal = false;\n" +
            "const logModalEl = document.getElementById('logModal');\n" +
            "logModalEl.addEventListener('mousedown', function(e) { isDraggingModal = (e.target !== this); });\n" +
            "logModalEl.addEventListener('click', function(e) {\n" +
            "  if (e.target === this && !isDraggingModal && !window.getSelection().toString().trim()) this.close();\n" +
            "  isDraggingModal = false;\n" +
            "});\n" +
            "</script>";

        // ── Header Assembly ───────────────────────────────────────────────────
        var header =
            HtmlFragments.Breadcrumbs(PathPrefix, ("Recurring", $"{PathPrefix}/recurring"), (job.RecurringJobId, null)) +
            $"<div style=\"display:flex;align-items:flex-start;justify-content:space-between;gap:20px;margin-bottom:28px;flex-wrap:wrap\">" +
            $"<div style=\"flex:1\">" +
            $"<h1 class=\"page-title\" style=\"margin-bottom:8px;font-size:28px\">{encodedId}</h1>" +
            $"<div style=\"display:flex;align-items:center;gap:12px;flex-wrap:wrap\">" +
            $"{stateBadge}" +
            $"{lastExecBadge}" +
            $"<span style=\"font-family:monospace;font-size:12px;color:var(--text-secondary);background:var(--bg-tertiary);padding:3px 8px;border-radius:4px;border:1px solid var(--border)\">" +
            $"{System.Web.HttpUtility.HtmlEncode(Helpers.ShortType(job.JobType))}" +
            $"</span>" +
            $"</div>" +
            $"</div>" +
            (actionsHtml.Length > 0
                ? $"<div style=\"display:flex;gap:8px;align-items:flex-start;flex-wrap:wrap;flex-shrink:0\">{actionsHtml}</div>"
                : string.Empty) +
            $"</div>";

        var body =
            (isReadOnly ? HtmlFragments.ReadOnlyBanner() : string.Empty) +
            $"<div id=\"recurring-detail-content\" data-refresh=\"true\">" +
            header +
            editPanel +
            overviewCards +
            errorBanner +
            executionsSection +
            $"</div>" +
            modalHtml;

        return HtmlShell.Wrap(Title, PathPrefix, "recurring", body, Counters, clusters: Clusters, activeCluster: ActiveCluster);
    }

    private string BuildPagination(PagedResult<JobRecord> result, string encodedIdUrl, string clusterParam)
    {
        var totalPages = (int)Math.Ceiling((double)result.TotalCount / result.PageSize);
        if (totalPages <= 1)
        {
            return string.Empty;
        }

        var prev = result.Page > 1
            ? $"<a href=\"{PathPrefix}/recurring/{encodedIdUrl}?page={result.Page - 1}&pageSize={PageSize}{clusterParam}\" class=\"btn btn-secondary btn-sm\">← Prev</a>"
            : string.Empty;

        var next = result.Page < totalPages
            ? $"<a href=\"{PathPrefix}/recurring/{encodedIdUrl}?page={result.Page + 1}&pageSize={PageSize}{clusterParam}\" class=\"btn btn-secondary btn-sm\">Next →</a>"
            : string.Empty;

        return
            $"<div style=\"padding:12px 20px;border-top:1px solid var(--border);display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:12px\">" +
            $"<span style=\"font-size:12px;color:var(--text-secondary)\">Page {result.Page} of {totalPages} ({result.TotalCount} total)</span>" +
            $"<div style=\"display:flex;gap:8px\">{prev}{next}</div>" +
            "</div>";
    }
}
