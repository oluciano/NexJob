using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Configuration;

namespace NexJob.Dashboard.Pages;

/// <summary>
/// Dashboard page that displays and modifies live runtime settings via
/// <see cref="IRuntimeSettingsStore"/>. Changes take effect immediately without restart.
/// </summary>
internal sealed class SettingsPage : ComponentBase
{
    private static readonly JsonSerializerOptions PrettyPrint = new() { WriteIndented = true };

    /// <summary>The runtime settings store injected by the middleware.</summary>
    [Parameter]
    public IRuntimeSettingsStore RuntimeStore { get; set; } = null!;

    /// <summary>The static NexJob options (code/appsettings baseline).</summary>
    [Parameter]
    public NexJobOptions Options { get; set; } = null!;

    /// <summary>Current runtime settings snapshot.</summary>
    [Parameter]
    public RuntimeSettings Runtime { get; set; } = null!;

    /// <summary>The dashboard URL path prefix.</summary>
    [Parameter]
    public string PathPrefix { get; set; } = string.Empty;

    /// <summary>Shared navigation counters.</summary>
    [Parameter]
    public NavCounters? Counters { get; set; }

    /// <summary>The dashboard title.</summary>
    [Parameter]
    public string Title { get; set; } = "NexJob";

    /// <inheritdoc/>
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var effectiveWorkers = Runtime.Workers ?? Options.Workers;
        var effectivePolling = (Runtime.PollingInterval ?? Options.PollingInterval).TotalSeconds;
        var effectiveRetentionSucceeded = (int)(Runtime.RetentionSucceeded ?? Options.RetentionSucceeded).TotalDays;
        var effectiveRetentionFailed = (int)(Runtime.RetentionFailed ?? Options.RetentionFailed).TotalDays;
        var effectiveRetentionExpired = (int)(Runtime.RetentionExpired ?? Options.RetentionExpired).TotalDays;

        var effectiveJson = JsonSerializer.Serialize(new
        {
            Workers = effectiveWorkers,
            PollingIntervalSeconds = effectivePolling,
            Options.MaxAttempts,
            Options.HeartbeatInterval,
            Options.HeartbeatTimeout,
            Queues = Options.Queues,
            PausedQueues = Runtime.PausedQueues,
            RecurringJobsPaused = Runtime.RecurringJobsPaused,
            RetentionSucceededDays = effectiveRetentionSucceeded,
            RetentionFailedDays = effectiveRetentionFailed,
            RetentionExpiredDays = effectiveRetentionExpired,
            RuntimeOverrides = new
            {
                Runtime.Workers,
                Runtime.PollingInterval,
                Runtime.RetentionSucceeded,
                Runtime.RetentionFailed,
                Runtime.RetentionExpired,
                Runtime.UpdatedAt,
            },
        }, PrettyPrint);

        var hasOverrides = Runtime.Workers.HasValue
            || Runtime.PollingInterval.HasValue
            || Runtime.PausedQueues.Count > 0
            || Runtime.RetentionSucceeded.HasValue
            || Runtime.RetentionFailed.HasValue
            || Runtime.RetentionExpired.HasValue;

        var body =
            "<div class=\"page-header\"><div>" +
            "<h1 class=\"page-title\">Settings</h1>" +
            "<p class=\"page-subtitle\">Live runtime configuration — changes apply immediately</p>" +
            "</div></div>" +

            // Workers card
            "<div class=\"settings-card\">" +
            "<div class=\"settings-card-header\">Workers</div>" +
            "<div class=\"settings-card-body\">" +
            "<div class=\"settings-row\">" +
            "<div class=\"settings-row-label\"><div>Active workers</div>" +
            $"<div class=\"settings-row-sub\">Baseline from config: {Options.Workers}</div></div>" +
            $"<form method=\"post\" action=\"{PathPrefix}/settings/workers\" style=\"display:flex;gap:8px;align-items:center\">" +
            $"<input type=\"number\" name=\"workers\" value=\"{effectiveWorkers}\" min=\"1\" max=\"200\" " +
            $"style=\"width:80px;padding:5px 8px;background:var(--surface2);border:1px solid var(--border);border-radius:6px;color:var(--text);font-size:12px\"/>" +
            "<button class=\"btn btn-primary btn-sm\" type=\"submit\">Apply</button>" +
            "</form>" +
            "</div>" +
            (Runtime.Workers.HasValue
                ? "<div class=\"settings-row\"><div class=\"alert alert-warning\" style=\"margin:0;flex:1\">⚠ Runtime override active — differs from appsettings baseline</div></div>"
                : string.Empty) +
            "</div></div>" +

            // Polling card
            "<div class=\"settings-card\">" +
            "<div class=\"settings-card-header\">Polling Interval</div>" +
            "<div class=\"settings-card-body\">" +
            "<div class=\"settings-row\">" +
            "<div class=\"settings-row-label\"><div>Polling interval (seconds)</div>" +
            $"<div class=\"settings-row-sub\">Baseline from config: {Options.PollingInterval.TotalSeconds}s</div></div>" +
            $"<form method=\"post\" action=\"{PathPrefix}/settings/polling\" style=\"display:flex;gap:8px;align-items:center\">" +
            $"<input type=\"number\" name=\"seconds\" value=\"{(int)effectivePolling}\" min=\"1\" max=\"300\" " +
            $"style=\"width:80px;padding:5px 8px;background:var(--surface2);border:1px solid var(--border);border-radius:6px;color:var(--text);font-size:12px\"/>" +
            "<span style=\"font-size:12px;color:var(--text-3)\">s</span>" +
            "<button class=\"btn btn-primary btn-sm\" type=\"submit\">Apply</button>" +
            "</form>" +
            "</div></div></div>" +

            // Queues card
            "<div class=\"settings-card\">" +
            "<div class=\"settings-card-header\">Queues</div>" +
            "<div class=\"settings-card-body\">" +
            BuildQueueRows() +
            "</div></div>" +

            // Retention card
            "<div class=\"settings-card\">" +
            "<div class=\"settings-card-header\">Retention Policy</div>" +
            "<div class=\"settings-card-body\">" +
            "<div class=\"settings-row\">" +
            "<div class=\"settings-row-label\"><div>Succeeded jobs (days)</div>" +
            $"<div class=\"settings-row-sub\">Baseline from config: {Options.RetentionSucceeded.TotalDays}d</div></div>" +
            $"<form method=\"post\" action=\"{PathPrefix}/settings/retention\" style=\"display:flex;gap:8px;align-items:center\">" +
            $"<input type=\"hidden\" name=\"retentionFailedDays\" value=\"{effectiveRetentionFailed}\" />" +
            $"<input type=\"hidden\" name=\"retentionExpiredDays\" value=\"{effectiveRetentionExpired}\" />" +
            $"<input type=\"number\" name=\"retentionSucceededDays\" value=\"{effectiveRetentionSucceeded}\" min=\"0\" max=\"3650\" " +
            $"style=\"width:80px;padding:5px 8px;background:var(--surface2);border:1px solid var(--border);border-radius:6px;color:var(--text);font-size:12px\"/>" +
            "<span style=\"font-size:12px;color:var(--text-3)\">days</span>" +
            "<button class=\"btn btn-primary btn-sm\" type=\"submit\">Save</button>" +
            "</form>" +
            "</div>" +
            "<div class=\"settings-row\">" +
            "<div class=\"settings-row-label\"><div>Failed jobs (days)</div>" +
            $"<div class=\"settings-row-sub\">Baseline from config: {Options.RetentionFailed.TotalDays}d</div></div>" +
            $"<form method=\"post\" action=\"{PathPrefix}/settings/retention\" style=\"display:flex;gap:8px;align-items:center\">" +
            $"<input type=\"hidden\" name=\"retentionSucceededDays\" value=\"{effectiveRetentionSucceeded}\" />" +
            $"<input type=\"hidden\" name=\"retentionExpiredDays\" value=\"{effectiveRetentionExpired}\" />" +
            $"<input type=\"number\" name=\"retentionFailedDays\" value=\"{effectiveRetentionFailed}\" min=\"0\" max=\"3650\" " +
            $"style=\"width:80px;padding:5px 8px;background:var(--surface2);border:1px solid var(--border);border-radius:6px;color:var(--text);font-size:12px\"/>" +
            "<span style=\"font-size:12px;color:var(--text-3)\">days</span>" +
            "<button class=\"btn btn-primary btn-sm\" type=\"submit\">Save</button>" +
            "</form>" +
            "</div>" +
            "<div class=\"settings-row\">" +
            "<div class=\"settings-row-label\"><div>Expired jobs (days)</div>" +
            $"<div class=\"settings-row-sub\">Baseline from config: {Options.RetentionExpired.TotalDays}d</div></div>" +
            $"<form method=\"post\" action=\"{PathPrefix}/settings/retention\" style=\"display:flex;gap:8px;align-items:center\">" +
            $"<input type=\"hidden\" name=\"retentionSucceededDays\" value=\"{effectiveRetentionSucceeded}\" />" +
            $"<input type=\"hidden\" name=\"retentionFailedDays\" value=\"{effectiveRetentionFailed}\" />" +
            $"<input type=\"number\" name=\"retentionExpiredDays\" value=\"{effectiveRetentionExpired}\" min=\"0\" max=\"3650\" " +
            $"style=\"width:80px;padding:5px 8px;background:var(--surface2);border:1px solid var(--border);border-radius:6px;color:var(--text);font-size:12px\"/>" +
            "<span style=\"font-size:12px;color:var(--text-3)\">days</span>" +
            "<button class=\"btn btn-primary btn-sm\" type=\"submit\">Save</button>" +
            "</form>" +
            "</div>" +
            (Runtime.RetentionSucceeded.HasValue || Runtime.RetentionFailed.HasValue || Runtime.RetentionExpired.HasValue
                ? "<div class=\"settings-row\"><div class=\"alert alert-warning\" style=\"margin:0;flex:1\">⚠ Runtime override active — differs from appsettings baseline</div></div>"
                : string.Empty) +
            "<div class=\"settings-row\" style=\"color:var(--text-3);font-size:12px;margin-top:8px\"><p style=\"margin:0\">Set to 0 to disable purging for that status</p></div>" +
            "</div></div>" +

            // Recurring jobs card
            "<div class=\"settings-card\">" +
            "<div class=\"settings-card-header\">Recurring Jobs</div>" +
            "<div class=\"settings-card-body\">" +
            "<div class=\"settings-row\">" +
            "<div class=\"settings-row-label\">" +
            $"<div>Recurring jobs</div><div class=\"settings-row-sub\">{(Runtime.RecurringJobsPaused ? "Currently paused — no new executions will fire" : "All recurring jobs are running normally")}</div>" +
            "</div>" +
            (Runtime.RecurringJobsPaused
                ? $"<span class=\"badge badge-processing\" style=\"margin-right:8px\">Paused</span><form method=\"post\" action=\"{PathPrefix}/recurring/resume-all\" style=\"display:inline\"><button class=\"btn btn-primary btn-sm\" type=\"submit\">Resume All</button></form>"
                : $"<span class=\"badge badge-succeeded\" style=\"margin-right:8px\">Active</span><form method=\"post\" action=\"{PathPrefix}/recurring/pause-all\" style=\"display:inline\"><button class=\"btn btn-danger btn-sm\" type=\"submit\">Pause All</button></form>") +
            "</div></div></div>" +

            // Effective configuration
            "<div class=\"settings-card\">" +
            "<div class=\"settings-card-header\">Effective Configuration</div>" +
            "<div class=\"settings-card-body\" style=\"padding:16px\">" +
            $"<pre style=\"margin:0\">{System.Web.HttpUtility.HtmlEncode(effectiveJson)}</pre>" +
            "</div></div>" +

            // Reset overrides
            (hasOverrides
                ? "<div style=\"margin-top:8px\">" +
                  $"<form method=\"post\" action=\"{PathPrefix}/settings/reset\">" +
                  "<button class=\"btn btn-danger\" type=\"submit\" onclick=\"return confirm('Reset all runtime overrides to baseline config?')\">" +
                  "Reset All Runtime Overrides</button>" +
                  "</form></div>"
                : string.Empty);

        builder.AddMarkupContent(0, HtmlShell.Wrap(Title, PathPrefix, "settings", body, Counters));
    }

    private string BuildQueueRows()
    {
        if (Options.Queues.Count == 0)
        {
            return "<div class=\"settings-row\"><span style=\"color:var(--text-3);font-size:12px\">No queues configured.</span></div>";
        }

        var sb = new System.Text.StringBuilder();
        var now = DateTimeOffset.UtcNow;

        foreach (var q in Options.Queues)
        {
            var isPaused = Runtime.PausedQueues.Contains(q);
            var windowSetting = Options.QueueSettings.Find(qs => string.Equals(qs.Name, q, StringComparison.Ordinal));
            var inWindow = windowSetting?.ExecutionWindow?.IsWithinWindow(now) ?? true;

            string statusBadge;
            if (isPaused)
            {
                statusBadge = "<span class=\"badge badge-processing\">Paused</span>";
            }
            else if (!inWindow)
            {
                statusBadge = "<span class=\"badge badge-scheduled\">Outside Window</span>";
            }
            else
            {
                statusBadge = "<span class=\"badge badge-succeeded\">Active</span>";
            }

            var toggleAction = isPaused
                ? $"{PathPrefix}/queues/{Uri.EscapeDataString(q)}/resume"
                : $"{PathPrefix}/queues/{Uri.EscapeDataString(q)}/pause";
            var toggleChecked = !isPaused ? " checked" : string.Empty;

            sb.Append(
                $"<div class=\"settings-row\">" +
                $"<div class=\"settings-row-label\">" +
                $"<div>{System.Web.HttpUtility.HtmlEncode(q)}</div>" +
                $"</div>" +
                $"{statusBadge}" +
                $"<form method=\"post\" action=\"{toggleAction}\" style=\"display:inline;margin-left:8px\">" +
                $"<label class=\"toggle\" title=\"{(isPaused ? "Resume" : "Pause")} queue\">" +
                $"<input type=\"checkbox\"{toggleChecked} onchange=\"this.form.submit()\" />" +
                $"<span class=\"toggle-thumb\"></span>" +
                $"</label></form>" +
                $"</div>");
        }

        return sb.ToString();
    }
}
