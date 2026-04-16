using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace NexJob.Dashboard.Pages;

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

        // ── State badges ──────────────────────────────────────────────────────

        string stateBadge;
        if (job.DeletedByUser)
        {
            stateBadge = "<span class=\"badge badge-failed\">Deleted</span>";
        }
        else if (!job.Enabled)
        {
            stateBadge = "<span class=\"badge\" style=\"background:var(--warning,#f59e0b);color:#000\">Paused</span>";
        }
        else
        {
            stateBadge = "<span class=\"badge badge-succeeded\">Enabled</span>";
        }

        var lastExecBadge = job.LastExecutionStatus switch
        {
            JobStatus.Succeeded => "<span class=\"badge badge-succeeded\" style=\"margin-left:6px\">✓ ok</span>",
            JobStatus.Failed => $"<span class=\"badge badge-failed\" style=\"margin-left:6px\" title=\"{System.Web.HttpUtility.HtmlAttributeEncode(job.LastExecutionError ?? string.Empty)}\">✗ err</span>",
            _ => string.Empty,
        };

        // ── Definition grid ───────────────────────────────────────────────────

        var cronOverrideNote = job.CronOverride is not null
            ? $"&nbsp;<span style=\"color:var(--text-muted);font-size:12px\">(override; default: <code>{System.Web.HttpUtility.HtmlEncode(job.Cron)}</code>)</span>"
            : string.Empty;

        var nextRunCell = job.NextExecution.HasValue && job.Enabled && !job.DeletedByUser
            ? $"{job.NextExecution.Value:yyyy-MM-dd HH:mm:ss UTC} &nbsp;·&nbsp; {Helpers.FormatCountdown(job.NextExecution.Value - now)}"
            : "<span style=\"color:var(--text-muted)\">—</span>";

        var lastRunCell = job.LastExecutedAt.HasValue
            ? job.LastExecutedAt.Value.ToString("yyyy-MM-dd HH:mm:ss UTC")
            : "<span style=\"color:var(--text-muted)\">never</span>";

        var defRows = new[]
        {
            ("Cron", $"<code style=\"color:var(--warning)\">{System.Web.HttpUtility.HtmlEncode(effectiveCron)}</code>{cronOverrideNote}"),
            ("Next Run", nextRunCell),
            ("Queue", System.Web.HttpUtility.HtmlEncode(job.Queue)),
            ("Job Type", Helpers.ShortType(job.JobType)),
            ("Last Run", lastRunCell),
        };

        var defGrid = string.Join(string.Empty,
            defRows.Select(r => $"<div class=\"detail-label\">{r.Item1}</div><div class=\"detail-value\">{r.Item2}</div>"));

        // ── Action buttons ────────────────────────────────────────────────────

        string actionsHtml;
        if (job.DeletedByUser)
        {
            actionsHtml =
                $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/restore\" style=\"display:inline\">" +
                "<button type=\"submit\" class=\"btn btn-primary btn-sm\">↩ Restore</button></form>";
        }
        else
        {
            var triggerButton =
                $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/trigger\" style=\"display:inline\">" +
                "<button type=\"submit\" class=\"btn btn-primary btn-sm\">▶ Trigger Now</button></form>";

            var pauseResumeButton = job.Enabled
                ? $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/pause\" style=\"display:inline\">" +
                  "<button type=\"submit\" class=\"btn btn-sm\" style=\"background:var(--warning,#f59e0b);color:#000\">⏸ Pause</button></form>"
                : $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/resume\" style=\"display:inline\">" +
                  "<button type=\"submit\" class=\"btn btn-primary btn-sm\">▶ Resume</button></form>";

            var forceDeleteButton =
                $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/force-delete\" style=\"display:inline\">" +
                "<button type=\"submit\" class=\"btn btn-danger btn-sm\" onclick=\"return confirm('Delete this job and all its records?')\">✕ Force Delete</button></form>";

            var editForm =
                $"<details style=\"display:inline-block;margin-left:4px\">" +
                $"<summary class=\"btn btn-sm\" style=\"cursor:pointer;display:inline-block\">✎ Edit Cron</summary>" +
                $"<form method=\"post\" action=\"{PathPrefix}/recurring/{encodedIdUrl}/update-config\" " +
                $"style=\"margin-top:6px;display:flex;gap:6px;align-items:center\">" +
                $"<input type=\"text\" name=\"cronOverride\" placeholder=\"{System.Web.HttpUtility.HtmlAttributeEncode(effectiveCron)}\" " +
                $"value=\"{System.Web.HttpUtility.HtmlAttributeEncode(job.CronOverride ?? string.Empty)}\" " +
                $"style=\"font-family:monospace;width:160px\" />" +
                $"<button type=\"submit\" class=\"btn btn-primary btn-sm\">Save</button>" +
                $"<button type=\"submit\" name=\"cronOverride\" value=\"\" class=\"btn btn-sm\">Reset to default</button>" +
                $"</form></details>";

            actionsHtml =
                $"<div style=\"display:flex;gap:6px;flex-wrap:wrap;align-items:center\">" +
                triggerButton +
                pauseResumeButton +
                forceDeleteButton +
                editForm +
                "</div>";
        }

        // ── Executions table ──────────────────────────────────────────────────

        string executionsSection;
        if (Executions.Items.Count == 0)
        {
            executionsSection =
                "<h2 style=\"margin-top:32px\">Last Executions</h2>" +
                "<p style=\"color:var(--text-muted);margin-top:8px\">No executions recorded yet.</p>";
        }
        else
        {
            var rows = string.Join(string.Empty, Executions.Items.Select((exec, idx) =>
            {
                var rowNum = ((Executions.Page - 1) * Executions.PageSize) + idx + 1;
                var duration = exec.ProcessingStartedAt.HasValue && exec.CompletedAt.HasValue
                    ? $"{(exec.CompletedAt.Value - exec.ProcessingStartedAt.Value).TotalSeconds:F1}s"
                    : "<span style=\"color:var(--text-muted)\">—</span>";
                var started = exec.ProcessingStartedAt.HasValue
                    ? exec.ProcessingStartedAt.Value.ToString("yyyy-MM-dd HH:mm:ss UTC")
                    : "<span style=\"color:var(--text-muted)\">—</span>";

                return
                    $"<tr>" +
                    $"<td>{rowNum}</td>" +
                    $"<td>{Helpers.BadgeHtml(exec.Status)}</td>" +
                    $"<td>{started}</td>" +
                    $"<td>{duration}</td>" +
                    $"<td><button onclick=\"showLogs('{exec.Id.Value}')\" style=\"background:none;border:none;color:#60a5fa;cursor:pointer;font-size:13px;padding:0\">View logs →</button></td>" +
                    $"</tr>";
            }));

            var opt10 = PageSize == 10 ? " selected" : string.Empty;
            var opt20 = PageSize == 20 ? " selected" : string.Empty;
            var opt50 = PageSize == 50 ? " selected" : string.Empty;

            var pageSizeSelector =
                $"<form method=\"get\" action=\"{PathPrefix}/recurring/{encodedIdUrl}\" style=\"display:inline-flex;align-items:center;gap:8px;margin-bottom:12px\">" +
                "<label style=\"color:#9ca3af;font-size:13px\">Show:</label>" +
                "<select name=\"pageSize\" onchange=\"this.form.submit()\" style=\"background:#1f2937;color:#f9fafb;border:1px solid #374151;border-radius:4px;padding:4px 8px;font-size:13px\">" +
                $"<option value=\"10\"{opt10}>10</option>" +
                $"<option value=\"20\"{opt20}>20</option>" +
                $"<option value=\"50\"{opt50}>50</option>" +
                "</select>" +
                "<input type=\"hidden\" name=\"page\" value=\"1\" />" +
                "<label style=\"color:#9ca3af;font-size:13px\">per page</label>" +
                "</form>";

            var tableHtml =
                "<table><thead><tr>" +
                "<th>#</th><th>Status</th><th>Started</th><th>Duration</th><th></th>" +
                $"</tr></thead><tbody>{rows}</tbody></table>";

            var paginationHtml = BuildPagination(Executions, encodedIdUrl);

            executionsSection =
                "<h2 style=\"margin-top:32px\">Last Executions</h2>" +
                $"<div class=\"section\" style=\"margin-top:12px\">{pageSizeSelector}{tableHtml}{paginationHtml}</div>";
        }

        // ── Modal + script ────────────────────────────────────────────────────

        var modalHtml =
            "<style>#logModal{position:fixed;top:50%;left:50%;transform:translate(-50%,-50%);margin:0}#logModal::backdrop{background:rgba(0,0,0,.6)}</style>" +
            "<dialog id=\"logModal\" style=\"background:#111827;color:#e5e7eb;border:1px solid #374151;border-radius:8px;padding:0;max-width:800px;width:90vw;max-height:80vh\">" +
            "<div style=\"display:flex;justify-content:space-between;align-items:center;padding:16px 20px;border-bottom:1px solid #374151\">" +
            "<h3 id=\"logModalTitle\" style=\"margin:0;font-size:16px;color:#f9fafb\">Execution Logs</h3>" +
            "<button onclick=\"document.getElementById('logModal').close()\" style=\"background:none;border:none;color:#9ca3af;cursor:pointer;font-size:20px;line-height:1\">&#x00D7;</button>" +
            "</div>" +
            "<div style=\"padding:16px 20px;overflow-y:auto;max-height:calc(80vh - 60px)\">" +
            "<pre id=\"logModalContent\" style=\"background:#0f172a;color:#e5e7eb;border:1px solid #1e293b;border-radius:6px;padding:12px 16px;font-family:monospace;font-size:12px;line-height:1.6;white-space:pre-wrap;word-break:break-all;margin:0\"></pre>" +
            "</div>" +
            "</dialog>" +
            $"<script>\nasync function showLogs(jobId) {{\n" +
            $"  const modal = document.getElementById('logModal');\n" +
            $"  const content = document.getElementById('logModalContent');\n" +
            $"  const title = document.getElementById('logModalTitle');\n" +
            $"  content.textContent = 'Loading...';\n" +
            $"  title.textContent = 'Execution Logs \\u2014 ' + jobId.substring(0, 8) + '...';\n" +
            $"  modal.showModal();\n" +
            $"  try {{\n" +
            $"    const res = await fetch(`{PathPrefix}/jobs/${{jobId}}/logs`);\n" +
            $"    const logs = await res.json();\n" +
            $"    if (logs.length === 0) {{\n" +
            $"      content.textContent = 'No logs captured for this execution.';\n" +
            $"      return;\n" +
            $"    }}\n" +
            $"    const levelColors = {{\n" +
            $"      'Trace': '#6b7280', 'Debug': '#6b7280', 'Information': '#e5e7eb',\n" +
            $"      'Warning': '#fbbf24', 'Error': '#f87171', 'Critical': '#f87171'\n" +
            $"    }};\n" +
            $"    content.innerHTML = '';\n" +
            $"    for (const log of logs) {{\n" +
            $"      const color = levelColors[log.level] || '#e5e7eb';\n" +
            $"      const levelPad = log.level.padEnd(11);\n" +
            $"      const span = document.createElement('span');\n" +
            $"      span.style.color = color;\n" +
            $"      span.textContent = `[${{log.timestamp}}] [${{levelPad}}] ${{log.message}}\\n`;\n" +
            $"      content.appendChild(span);\n" +
            $"    }}\n" +
            $"  }} catch (e) {{\n" +
            $"    content.textContent = 'Failed to load logs.';\n" +
            $"  }}\n" +
            $"}}\n" +
            $"document.getElementById('logModal').addEventListener('click', function(e) {{\n" +
            $"  if (e.target === this) this.close();\n" +
            $"}});\n" +
            "</script>";

        // ── Assemble body ─────────────────────────────────────────────────────

        var body =
            HtmlFragments.Breadcrumbs(PathPrefix, ("Recurring", PathPrefix + "/recurring"), (job.RecurringJobId, null)) +
            $"<div id=\"auto-refresh-container\" data-refresh=\"true\">" +
            $"<div style=\"display:flex;align-items:center;gap:12px;margin-top:16px;margin-bottom:8px\">" +
            $"<h1 class=\"page-title\" style=\"margin-bottom:0\">{encodedId}</h1>" +
            $"{stateBadge}{lastExecBadge}" +
            $"</div>" +
            $"<div style=\"margin-top:16px\">" +
            $"<div class=\"detail-grid\">{defGrid}</div>" +
            $"</div>" +
            $"<div style=\"margin-top:20px\">{actionsHtml}</div>" +
            executionsSection +
            $"</div>" +
            modalHtml;

        return HtmlShell.Wrap(Title, PathPrefix, "recurring", body, Counters);
    }

    private string BuildPagination(PagedResult<JobRecord> result, string encodedIdUrl)
    {
        var totalPages = (int)Math.Ceiling((double)result.TotalCount / result.PageSize);
        if (totalPages <= 1)
        {
            return string.Empty;
        }

        var prev = result.Page > 1
            ? $"<a href=\"{PathPrefix}/recurring/{encodedIdUrl}?page={result.Page - 1}&pageSize={PageSize}\" class=\"btn btn-sm\">← Prev</a>"
            : string.Empty;

        var next = result.Page < totalPages
            ? $"<a href=\"{PathPrefix}/recurring/{encodedIdUrl}?page={result.Page + 1}&pageSize={PageSize}\" class=\"btn btn-sm\">Next →</a>"
            : string.Empty;

        return
            $"<div class=\"pagination\">" +
            prev +
            $"<span class=\"page-info\">Page {result.Page} of {totalPages} ({result.TotalCount} total)</span>" +
            next +
            "</div>";
    }
}
