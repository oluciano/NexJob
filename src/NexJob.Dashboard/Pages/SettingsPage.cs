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

    /// <summary>The dashboard title.</summary>
    [Parameter]
    public string Title { get; set; } = "NexJob";

    /// <inheritdoc/>
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var effectiveWorkers = Runtime.Workers ?? Options.Workers;
        var effectivePolling = (Runtime.PollingInterval ?? Options.PollingInterval).TotalSeconds;

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
            RuntimeOverrides = new
            {
                Runtime.Workers,
                Runtime.PollingInterval,
                Runtime.UpdatedAt,
            },
        }, PrettyPrint);

        var body = $"""
            <h1 class="page-title">Live Settings</h1>

            <div class="section">
              <h2>Workers</h2>
              <form method="post" action="{PathPrefix}/settings/workers">
                <div style="display:flex;gap:10px;align-items:center">
                  <input type="number" name="workers" value="{effectiveWorkers}" min="1" max="200"
                         style="width:100px;padding:6px 10px;background:var(--surface);border:1px solid var(--border);border-radius:6px;color:var(--text)"/>
                  <button class="btn btn-primary" type="submit">Apply</button>
                  {(Runtime.Workers.HasValue ? $"<span style='color:var(--warning);font-size:12px'>⚠ Runtime override active (baseline: {Options.Workers})</span>" : $"<span style='color:var(--text-muted);font-size:12px'>Baseline from config: {Options.Workers}</span>")}
                </div>
              </form>
            </div>

            <div class="section">
              <h2>Polling Interval</h2>
              <form method="post" action="{PathPrefix}/settings/polling">
                <div style="display:flex;gap:10px;align-items:center">
                  <input type="number" name="seconds" value="{(int)effectivePolling}" min="1" max="300"
                         style="width:100px;padding:6px 10px;background:var(--surface);border:1px solid var(--border);border-radius:6px;color:var(--text)"/>
                  <span style="color:var(--text-muted)">seconds</span>
                  <button class="btn btn-primary" type="submit">Apply</button>
                  {(Runtime.PollingInterval.HasValue ? $"<span style='color:var(--warning);font-size:12px'>⚠ Runtime override active (baseline: {Options.PollingInterval.TotalSeconds}s)</span>" : string.Empty)}
                </div>
              </form>
            </div>

            <div class="section">
              <h2>Queues</h2>
              <table>
                <thead><tr><th>Queue</th><th>Status</th><th>Action</th></tr></thead>
                <tbody>
                  {BuildQueueRows()}
                </tbody>
              </table>
            </div>

            <div class="section">
              <h2>Recurring Jobs</h2>
              <div style="display:flex;gap:10px;align-items:center">
                {(Runtime.RecurringJobsPaused
                    ? $"<span class='badge badge-failed'>Paused</span><form method='post' action='{PathPrefix}/recurring/resume-all'><button class='btn btn-primary btn-sm' type='submit'>Resume All</button></form>"
                    : $"<span class='badge badge-succeeded'>Active</span><form method='post' action='{PathPrefix}/recurring/pause-all'><button class='btn btn-danger btn-sm' type='submit'>Pause All</button></form>")}
              </div>
            </div>

            <div class="section">
              <h2>Effective Configuration</h2>
              <pre>{System.Web.HttpUtility.HtmlEncode(effectiveJson)}</pre>
            </div>

            <div class="section">
              <form method="post" action="{PathPrefix}/settings/reset">
                <button class="btn btn-danger" type="submit"
                        onclick="return confirm('Reset all runtime overrides to baseline?')">
                  Reset All Runtime Overrides
                </button>
              </form>
            </div>
            """;

        builder.AddMarkupContent(0, HtmlShell.Wrap(Title, PathPrefix, "settings", body));
    }

    private string BuildQueueRows()
    {
        var rows = new System.Text.StringBuilder();
        var now = DateTimeOffset.UtcNow;

        foreach (var q in Options.Queues)
        {
            var isPaused = Runtime.PausedQueues.Contains(q);
            var windowSetting = Options.QueueSettings.Find(qs => qs.Name == q);
            var inWindow = windowSetting?.ExecutionWindow?.IsWithinWindow(now) ?? true;

            string status;
            if (isPaused)
            {
                status = "<span class='badge badge-failed'>Paused</span>";
            }
            else if (!inWindow)
            {
                status = "<span class='badge badge-scheduled'>Outside Window</span>";
            }
            else
            {
                status = "<span class='badge badge-succeeded'>Active</span>";
            }

            var toggleAction = isPaused
                ? $"<form method='post' action='{PathPrefix}/queues/{q}/resume' style='display:inline'><button class='btn btn-primary btn-sm'>Resume</button></form>"
                : $"<form method='post' action='{PathPrefix}/queues/{q}/pause' style='display:inline'><button class='btn btn-danger btn-sm'>Pause</button></form>";

            rows.AppendLine($"<tr><td>{q}</td><td>{status}</td><td>{toggleAction}</td></tr>");
        }

        return rows.ToString();
    }
}
