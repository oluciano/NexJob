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
            HtmlFragments.Breadcrumbs(PathPrefix, ("Settings", null)) +
            HtmlFragments.PageHeader("Settings", "Live runtime configuration — changes apply immediately") +

            "<div style=\"display:grid;grid-template-columns:repeat(auto-fit, minmax(400px, 1fr));gap:24px\">" +

            // Workers card
            "<div class=\"card\">" +
            "<div class=\"card-header\"><h3>Workers</h3></div>" +
            "<div style=\"padding:16px\">" +
            "<div style=\"display:flex;justify-content:space-between;align-items:center\">" +
            "<div><div style=\"font-weight:600\">Active workers</div>" +
            $"<div style=\"font-size:12px;color:var(--text-tertiary)\">Baseline: {Options.Workers}</div></div>" +
            $"<form method=\"post\" action=\"{PathPrefix}/settings/workers\" style=\"display:flex;gap:8px;align-items:center\">" +
            $"<input type=\"number\" name=\"workers\" value=\"{effectiveWorkers}\" min=\"1\" max=\"200\" " +
            $"style=\"width:80px\"/>" +
            "<button class=\"btn btn-primary btn-sm\" type=\"submit\">Apply</button>" +
            "</form>" +
            "</div>" +
            (Runtime.Workers.HasValue
                ? "<div style=\"margin-top:12px\"><div class=\"badge badge-warning\" style=\"width:100%;text-align:center\">Runtime override active</div></div>"
                : string.Empty) +
            "</div></div>" +

            // Polling card
            "<div class=\"card\">" +
            "<div class=\"card-header\"><h3>Polling Interval</h3></div>" +
            "<div style=\"padding:16px\">" +
            "<div style=\"display:flex;justify-content:space-between;align-items:center\">" +
            "<div><div style=\"font-weight:600\">Polling interval</div>" +
            $"<div style=\"font-size:12px;color:var(--text-tertiary)\">Baseline: {Options.PollingInterval.TotalSeconds}s</div></div>" +
            $"<form method=\"post\" action=\"{PathPrefix}/settings/polling\" style=\"display:flex;gap:8px;align-items:center\">" +
            $"<input type=\"number\" name=\"seconds\" value=\"{(int)effectivePolling}\" min=\"1\" max=\"300\" " +
            $"style=\"width:80px\"/>" +
            "<span style=\"font-size:12px;color:var(--text-tertiary)\">s</span>" +
            "<button class=\"btn btn-primary btn-sm\" type=\"submit\">Apply</button>" +
            "</form>" +
            "</div></div></div>" +

            // Queues card
            "<div class=\"card\">" +
            "<div class=\"card-header\"><h3>Queues</h3></div>" +
            "<div style=\"padding:16px\">" +
            BuildQueueRows() +
            "</div></div>" +

            // Retention card
            "<div class=\"card\">" +
            "<div class=\"card-header\"><h3>Retention Policy</h3></div>" +
            "<div style=\"padding:16px\">" +
            "<div style=\"display:flex;flex-direction:column;gap:12px\">" +
            BuildRetentionRow("Succeeded jobs (days)", "retentionSucceededDays", effectiveRetentionSucceeded, Options.RetentionSucceeded.TotalDays, effectiveRetentionFailed, effectiveRetentionExpired) +
            BuildRetentionRow("Failed jobs (days)", "retentionFailedDays", effectiveRetentionFailed, Options.RetentionFailed.TotalDays, effectiveRetentionSucceeded, effectiveRetentionExpired) +
            BuildRetentionRow("Expired jobs (days)", "retentionExpiredDays", effectiveRetentionExpired, Options.RetentionExpired.TotalDays, effectiveRetentionSucceeded, effectiveRetentionFailed) +
            "</div>" +
            (Runtime.RetentionSucceeded.HasValue || Runtime.RetentionFailed.HasValue || Runtime.RetentionExpired.HasValue
                ? "<div style=\"margin-top:12px\"><div class=\"badge badge-warning\" style=\"width:100%;text-align:center\">Runtime override active</div></div>"
                : string.Empty) +
            "</div></div>" +

            // Recurring jobs card
            "<div class=\"card\">" +
            "<div class=\"card-header\"><h3>Recurring Jobs</h3></div>" +
            "<div style=\"padding:16px\">" +
            "<div style=\"display:flex;justify-content:space-between;align-items:center\">" +
            "<div><div style=\"font-weight:600\">Execution status</div><div style=\"font-size:12px;color:var(--text-tertiary)\">" +
            $"{(Runtime.RecurringJobsPaused ? "Currently paused" : "Running normally")}</div>" +
            "</div>" +
            (Runtime.RecurringJobsPaused
                ? $"<form method=\"post\" action=\"{PathPrefix}/recurring/resume-all\"><button class=\"btn btn-primary btn-sm\" type=\"submit\">Resume All</button></form>"
                : $"<form method=\"post\" action=\"{PathPrefix}/recurring/pause-all\"><button class=\"btn btn-danger btn-sm\" type=\"submit\">Pause All</button></form>") +
            "</div></div></div>" +

            "</div>" + // End grid

            // Effective configuration
            "<div class=\"card\">" +
            "<div class=\"card-header\"><h3>Effective Configuration</h3></div>" +
            "<div style=\"padding:16px\">" +
            $"<pre style=\"margin:0;font-size:12px;background:var(--bg-tertiary);padding:16px;border-radius:8px;overflow:auto;max-height:400px\">{System.Web.HttpUtility.HtmlEncode(effectiveJson)}</pre>" +
            "</div></div>" +

            // Reset overrides
            (hasOverrides
                ? "<div style=\"margin-top:8px;text-align:right\">" +
                  $"<form method=\"post\" action=\"{PathPrefix}/settings/reset\">" +
                  "<button class=\"btn btn-danger\" type=\"submit\" onclick=\"return confirm('Reset all runtime overrides to baseline config?')\">" +
                  "Reset All Runtime Overrides</button>" +
                  "</form></div>"
                : string.Empty);

        builder.AddMarkupContent(0, HtmlShell.Wrap(Title, PathPrefix, "settings", body, Counters));
    }

    private string BuildRetentionRow(string label, string fieldName, int value, double baseline, int other1, int other2)
    {
        var otherFields = fieldName switch
        {
            "retentionSucceededDays" => $"<input type=\"hidden\" name=\"retentionFailedDays\" value=\"{other1}\" /><input type=\"hidden\" name=\"retentionExpiredDays\" value=\"{other2}\" />",
            "retentionFailedDays" => $"<input type=\"hidden\" name=\"retentionSucceededDays\" value=\"{other1}\" /><input type=\"hidden\" name=\"retentionExpiredDays\" value=\"{other2}\" />",
            _ => $"<input type=\"hidden\" name=\"retentionSucceededDays\" value=\"{other1}\" /><input type=\"hidden\" name=\"retentionFailedDays\" value=\"{other2}\" />",
        };

        return
            "<div style=\"display:flex;justify-content:space-between;align-items:center\">" +
            $"<div><div style=\"font-weight:600\">{label}</div>" +
            $"<div style=\"font-size:12px;color:var(--text-tertiary)\">Baseline: {baseline}d</div></div>" +
            $"<form method=\"post\" action=\"{PathPrefix}/settings/retention\" style=\"display:flex;gap:8px;align-items:center\">" +
            otherFields +
            $"<input type=\"number\" name=\"{fieldName}\" value=\"{value}\" min=\"0\" max=\"3650\" style=\"width:80px\"/>" +
            "<button class=\"btn btn-primary btn-sm\" type=\"submit\">Save</button>" +
            "</form></div>";
    }

    private string BuildQueueRows()
    {
        if (Options.Queues.Count == 0)
        {
            return "<div style=\"color:var(--text-tertiary);font-size:12px\">No queues configured.</div>";
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
                statusBadge = "<span class=\"badge badge-warning\">Paused</span>";
            }
            else if (!inWindow)
            {
                statusBadge = "<span class=\"badge badge-gray\">Outside Window</span>";
            }
            else
            {
                statusBadge = "<span class=\"badge badge-success\">Active</span>";
            }

            var toggleAction = isPaused
                ? $"{PathPrefix}/queues/{Uri.EscapeDataString(q)}/resume"
                : $"{PathPrefix}/queues/{Uri.EscapeDataString(q)}/pause";

            var toggleIcon = isPaused
                ? "<svg width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><polygon points=\"5 3 19 12 5 21 5 3\"/></svg>"
                : "<svg width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><rect x=\"6\" y=\"4\" width=\"4\" height=\"16\"/><rect x=\"14\" y=\"4\" width=\"4\" height=\"16\"/></svg>";

            var colorStyle = isPaused ? "color:var(--success)" : "color:var(--warning)";

            sb.Append(
                $"<div style=\"display:flex;justify-content:space-between;align-items:center;padding:6px 0;border-bottom:1px solid var(--border)\">" +
                $"<div><div style=\"font-weight:600\">{System.Web.HttpUtility.HtmlEncode(q)}</div></div>" +
                $"<div style=\"display:flex;align-items:center;gap:12px\">" +
                $"{statusBadge}" +
                $"<form method=\"post\" action=\"{toggleAction}\">" +
                $"<button type=\"submit\" class=\"btn-icon-sm\" title=\"{(isPaused ? "Resume" : "Pause")} queue\" style=\"{colorStyle}\">{toggleIcon}</button>" +
                $"</form></div></div>");
        }

        return sb.ToString();
    }
}
