using System.Diagnostics.CodeAnalysis;
using System.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

[ExcludeFromCodeCoverage]
internal sealed class JobDetailPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IDashboardStorage Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/dashboard";
    [Parameter] public string Title { get; set; } = "NexJob";
    [Parameter] public NavCounters? Counters { get; set; }
    [Parameter] public JobId JobId { get; set; }
    [Parameter] public bool IsReadOnly { get; set; }
    [Parameter] public IReadOnlyList<DashboardCluster>? Clusters { get; set; }
    [Parameter] public DashboardCluster? ActiveCluster { get; set; }

    /// <summary>Gets or sets the set of queue names that currently have at least one active worker listening.</summary>
    [Parameter] public IReadOnlySet<string>? ActiveWorkerQueues { get; set; }

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);

        // NOTE (Blazor Dispatcher Invariant):
        // Do NOT use .ConfigureAwait(false) here. Rendering via _handle.Render requires execution on the Dispatcher.
        var job = await Storage.GetJobByIdAsync(JobId);
        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(job)));
    }

    private string BuildHtml(JobRecord? job)
    {
        if (job is null)
        {
            var notFoundHtml =
                HtmlFragments.Breadcrumbs(PathPrefix, ("Jobs", $"{PathPrefix}/jobs"), ("Not Found", null)) +
                HtmlFragments.EmptyState(HtmlFragments.EmptyIconInbox, "Job not found") +
                $"<div style=\"text-align:center;margin-top:12px\"><a href=\"{PathPrefix}/jobs\" class=\"btn btn-ghost btn-sm\">← Back to Jobs</a></div>";
            return HtmlShell.Wrap(Title, PathPrefix, "jobs", notFoundHtml, Counters, clusters: Clusters, activeCluster: ActiveCluster);
        }

        var now = DateTimeOffset.UtcNow;
        var vm = new JobDetailViewModel { Job = job, PathPrefix = PathPrefix, Now = now };

        var clusterSuffix = ActiveCluster is not null ? $"?cluster={Uri.EscapeDataString(ActiveCluster.Id)}" : string.Empty;

        // Action buttons
        var actions = string.Empty;
        if (!IsReadOnly)
        {
            var queueOrphanWarning = ActiveWorkerQueues is { Count: > 0 } && !ActiveWorkerQueues.Contains(job.Queue, StringComparer.OrdinalIgnoreCase)
                ? $"\\n\\n⚠ Warning: Queue '{job.Queue}' has no active workers. The job will remain queued until a worker starts."
                : string.Empty;

            if (job.Status == JobStatus.Scheduled)
            {
                var runNowConfirm = $"Run this job now (bypass schedule)?{queueOrphanWarning}";
                actions +=
                    $"<form method=\"post\" action=\"{PathPrefix}/jobs/{job.Id.Value}/runnow{clusterSuffix}\" style=\"display:inline\">" +
                    $"<button type=\"submit\" class=\"btn btn-primary btn-sm\" onclick=\"return confirm({System.Web.HttpUtility.JavaScriptStringEncode(runNowConfirm, addDoubleQuotes: true)}\">▶ Run Now</button></form> " +
                    $"<form method=\"post\" action=\"{PathPrefix}/jobs/{job.Id.Value}/delete{clusterSuffix}\" style=\"display:inline\">" +
                    "<button type=\"submit\" class=\"btn btn-danger btn-sm\" onclick=\"return confirm('Cancel and delete this scheduled job?')\">Delete</button></form>";
            }
            else if (job.Status == JobStatus.Enqueued)
            {
                actions +=
                    $"<form method=\"post\" action=\"{PathPrefix}/jobs/{job.Id.Value}/delete{clusterSuffix}\" style=\"display:inline\">" +
                    "<button type=\"submit\" class=\"btn btn-danger btn-sm\" onclick=\"return confirm('Cancel and delete this enqueued job?')\">Delete</button></form>";
            }
            else if (job.Status == JobStatus.Failed)
            {
                var requeueConfirm = $"Requeue this job?{queueOrphanWarning}";
                actions +=
                    $"<form method=\"post\" action=\"{PathPrefix}/jobs/{job.Id.Value}/requeue{clusterSuffix}\" style=\"display:inline\">" +
                    $"<button type=\"submit\" class=\"btn btn-primary btn-sm\" onclick=\"return confirm({System.Web.HttpUtility.JavaScriptStringEncode(requeueConfirm, addDoubleQuotes: true)})\">↺ Requeue</button></form> " +
                    $"<form method=\"post\" action=\"{PathPrefix}/jobs/{job.Id.Value}/delete{clusterSuffix}\" style=\"display:inline\">" +
                    "<button type=\"submit\" class=\"btn btn-danger btn-sm\" onclick=\"return confirm('Delete this job?')\">Delete</button></form>";
            }
        }

        // Tags HTML
        var tagsHtml = job.Tags.Count > 0
            ? string.Join(" ", job.Tags.Select(t =>
                $"<span class=\"tag-badge\">{HttpUtility.HtmlEncode(t)}</span>"))
            : "—";

        // Header with Breadcrumbs
        var header =
            HtmlFragments.Breadcrumbs(PathPrefix, ("Jobs", $"{PathPrefix}/jobs"), (vm.ShortType, null)) +
            $"<div style=\"display:flex;align-items:center;justify-content:space-between;gap:20px;margin-bottom:24px;flex-wrap:wrap\">" +
            $"<div>" +
            $"<h1 class=\"page-title\" style=\"margin:0;font-size:28px\">{HttpUtility.HtmlEncode(vm.ShortType)}</h1>" +
            $"</div>" +
            (actions.Length > 0
                ? $"<div style=\"display:flex;gap:8px;align-items:center;flex-wrap:wrap;flex-shrink:0\">{actions}</div>"
                : string.Empty) +
            $"</div>";

        // Progress bar
        var progressSection = HtmlFragments.ProgressBar(job.ProgressPercent, job.ProgressMessage);

        // Visual execution flow timeline
        var executionTimeline = HtmlFragments.ExecutionTimeline(job, now);

        // ── 2-Column Inspector: Configuration & Telemetry ──────────────────────
        // 1. Configuration & Governance
        var hasWorker = ActiveWorkerQueues == null || ActiveWorkerQueues.Contains(job.Queue, StringComparer.OrdinalIgnoreCase);
        var workerIndicator = hasWorker
            ? "<div style=\"display:inline-flex;align-items:center;gap:6px;background:rgba(40,199,111,0.12);color:var(--success);padding:4px 10px;border-radius:20px;font-size:12px;font-weight:600;border:1px solid rgba(40,199,111,0.25)\"><span class=\"pulse-live\"></span> Active Workers Listening</div>"
            : "<div style=\"display:inline-flex;align-items:center;gap:6px;background:rgba(245,158,11,0.12);color:var(--warning);padding:4px 10px;border-radius:20px;font-size:12px;font-weight:600;border:1px solid rgba(245,158,11,0.25)\">⚠️ No Active Workers (Orphaned)</div>";

        string priorityBadge = job.Priority switch
        {
            JobPriority.High => "<span class=\"badge badge-error\" style=\"font-size:11px;padding:3px 10px;font-weight:700\">⚡ High Priority</span>",
            JobPriority.Low => "<span class=\"badge\" style=\"background:var(--bg-tertiary);color:var(--text-tertiary);border:1px solid var(--border);font-size:11px;padding:3px 10px\">💤 Low Priority</span>",
            _ => "<span class=\"badge badge-success\" style=\"font-size:11px;padding:3px 10px;font-weight:600\">✓ Normal Priority</span>",
        };

        // Retry Budget Segment Visualizer
        var effectiveMax = Helpers.GetEffectiveMaxAttempts(job);
        var budgetSegments = new System.Text.StringBuilder();
        var maxDots = Math.Min(effectiveMax, 12);
        for (int i = 1; i <= maxDots; i++)
        {
            string segColor;
            if (i < job.Attempts)
            {
                segColor = "var(--error)";
            }
            else if (i == job.Attempts)
            {
                segColor = job.Status switch
                {
                    JobStatus.Succeeded => "var(--success)",
                    JobStatus.Failed => "var(--error)",
                    _ => "var(--warning)",
                };
            }
            else
            {
                segColor = "var(--border)";
            }

            budgetSegments.Append($"<span style=\"flex:1;height:6px;background:{segColor};border-radius:3px;transition:var(--transition)\" title=\"Attempt {i}\"></span>");
        }

        var remainingAttempts = Math.Max(0, effectiveMax - job.Attempts);
        var attemptSuffix = remainingAttempts == 1 ? string.Empty : "s";
        string budgetLabel;
        if (job.Status == JobStatus.Failed && job.Attempts >= effectiveMax)
        {
            budgetLabel = "<span style=\"color:var(--error);font-weight:600\">Exhausted (Moved to dead-letter)</span>";
        }
        else if (job.Status == JobStatus.Succeeded)
        {
            budgetLabel = "<span style=\"color:var(--success);font-weight:600\">Succeeded</span>";
        }
        else
        {
            budgetLabel = $"<span>{remainingAttempts} attempt{attemptSuffix} remaining</span>";
        }

        string idempotencyHtml;
        if (job.IdempotencyKey is not null)
        {
            var encKeyJs = HttpUtility.JavaScriptStringEncode(job.IdempotencyKey);
            idempotencyHtml =
                $"<div style=\"padding:10px 12px;background:var(--bg-secondary);border:1px solid var(--border);border-radius:6px\">" +
                $"<div style=\"display:flex;align-items:center;justify-content:space-between;margin-bottom:6px\">" +
                $"<span style=\"font-size:11px;color:var(--text-tertiary);text-transform:uppercase;font-weight:700\">Unique Enqueue Key</span>" +
                $"<span style=\"font-size:11px;color:var(--success);font-weight:600\">🛡️ Deduplication Guard Active</span>" +
                $"</div>" +
                $"<div style=\"display:flex;align-items:center;gap:8px\">" +
                $"<code style=\"font-size:12px;font-family:monospace;color:var(--text-primary);background:var(--bg-tertiary);padding:4px 8px;border-radius:4px;border:1px solid var(--border);word-break:break-all;flex:1\">{HttpUtility.HtmlEncode(job.IdempotencyKey)}</code>" +
                $"<button class=\"btn btn-secondary btn-sm\" onclick=\"navigator.clipboard.writeText('{encKeyJs}');this.textContent='Copied!';setTimeout(()=>this.textContent='Copy',1200)\" title=\"Copy idempotency key\" style=\"font-size:11px;padding:3px 8px;flex-shrink:0\">Copy</button>" +
                $"</div>" +
                $"</div>";
        }
        else
        {
            idempotencyHtml = "<span style=\"color:var(--text-tertiary);font-size:13px\">None (Standard enqueue without idempotency lock)</span>";
        }

        // 2. Lineage & Telemetry
        string originHtml;
        if (job.RecurringJobId is not null)
        {
            var encRecId = Uri.EscapeDataString(job.RecurringJobId);
            originHtml =
                $"<div style=\"padding:12px 14px;background:var(--bg-secondary);border:1px solid var(--border);border-radius:8px;display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap\">" +
                $"<div style=\"display:flex;align-items:center;gap:10px\">" +
                $"<span style=\"font-size:20px\">🗓️</span>" +
                $"<div>" +
                $"<div style=\"font-size:11px;color:var(--text-tertiary);text-transform:uppercase;font-weight:700\">Scheduled Origin</div>" +
                $"<div style=\"font-size:13px;font-weight:600;color:var(--text-primary)\">Automated Recurring Trigger</div>" +
                $"</div>" +
                $"</div>" +
                $"<a href=\"{PathPrefix}/recurring/{encRecId}{clusterSuffix}\" class=\"btn btn-secondary btn-sm\" style=\"font-family:monospace;font-size:12px;font-weight:600;display:inline-flex;align-items:center;gap:6px\">" +
                $"{HttpUtility.HtmlEncode(job.RecurringJobId)} ➔</a>" +
                $"</div>";
        }
        else if (job.ParentJobId.HasValue)
        {
            var pId = job.ParentJobId.Value.Value.ToString();
            originHtml =
                $"<div style=\"padding:12px 14px;background:var(--bg-secondary);border:1px solid var(--border);border-radius:8px;display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap\">" +
                $"<div style=\"display:flex;align-items:center;gap:10px\">" +
                $"<span style=\"font-size:20px\">🔗</span>" +
                $"<div>" +
                $"<div style=\"font-size:11px;color:var(--text-tertiary);text-transform:uppercase;font-weight:700\">Workflow Continuation</div>" +
                $"<div style=\"font-size:13px;font-weight:600;color:var(--text-primary)\">Child Job Execution</div>" +
                $"</div>" +
                $"</div>" +
                $"<a href=\"{PathPrefix}/jobs/{pId}{clusterSuffix}\" class=\"btn btn-secondary btn-sm\" style=\"font-family:monospace;font-size:12px;font-weight:600;display:inline-flex;align-items:center;gap:6px\">" +
                $"Parent: {pId[..8]}... ➔</a>" +
                $"</div>";
        }
        else
        {
            originHtml =
                $"<div style=\"padding:12px 14px;background:var(--bg-secondary);border:1px solid var(--border);border-radius:8px;display:flex;align-items:center;gap:10px\">" +
                $"<span style=\"font-size:20px\">⚡</span>" +
                $"<div>" +
                $"<div style=\"font-size:11px;color:var(--text-tertiary);text-transform:uppercase;font-weight:700\">Direct Ingress</div>" +
                $"<div style=\"font-size:13px;font-weight:600;color:var(--text-primary)\">Enqueued programmatically via IScheduler API</div>" +
                $"</div>" +
                $"</div>";
        }

        // Distributed Tracing (W3C OpenTelemetry)
        var (traceId, spanId, sampled) = Helpers.ParseTraceParent(job.TraceParent);
        string telemetryHtml;
        if (!string.IsNullOrEmpty(traceId))
        {
            var encTraceJs = HttpUtility.JavaScriptStringEncode(job.TraceParent ?? string.Empty);
            var sampleBadge = sampled
                ? "<span class=\"badge badge-success\" style=\"font-size:10px;padding:1px 6px\">Sampled (Recorded)</span>"
                : "<span class=\"badge\" style=\"font-size:10px;padding:1px 6px;background:var(--bg-tertiary);color:var(--text-tertiary)\">Not Sampled</span>";

            telemetryHtml =
                $"<div style=\"padding:12px 14px;background:var(--bg-secondary);border:1px solid var(--border);border-radius:8px\">" +
                $"<div style=\"display:flex;align-items:center;justify-content:space-between;margin-bottom:8px\">" +
                $"<span style=\"font-size:11px;font-weight:700;color:var(--text-tertiary);text-transform:uppercase\">W3C Traceparent Header</span>" +
                $"<span class=\"badge\" style=\"background:rgba(59,130,246,0.12);color:var(--primary);border:1px solid rgba(59,130,246,0.25);font-size:10px\">OpenTelemetry Native</span>" +
                $"</div>" +
                $"<div style=\"display:grid;grid-template-columns:auto 1fr;gap:6px 12px;font-family:monospace;font-size:11px;align-items:center\">" +
                $"<span style=\"color:var(--text-tertiary)\">Trace ID:</span>" +
                $"<code style=\"color:var(--text-primary);background:var(--bg-tertiary);padding:2px 6px;border-radius:4px;word-break:break-all\">{traceId}</code>" +
                $"<span style=\"color:var(--text-tertiary)\">Span ID:</span>" +
                $"<code style=\"color:var(--text-primary);background:var(--bg-tertiary);padding:2px 6px;border-radius:4px\">{spanId}</code>" +
                $"</div>" +
                $"<div style=\"display:flex;justify-content:space-between;align-items:center;margin-top:10px;padding-top:8px;border-top:1px solid var(--border)\">" +
                $"<div>{sampleBadge}</div>" +
                $"<button class=\"btn btn-secondary btn-sm\" onclick=\"navigator.clipboard.writeText('{encTraceJs}');this.textContent='Copied!';setTimeout(()=>this.textContent='Copy TraceParent',1500)\" style=\"font-size:11px;padding:2px 8px\">Copy TraceParent</button>" +
                $"</div>" +
                $"</div>";
        }
        else
        {
            telemetryHtml =
                $"<div style=\"font-size:12px;color:var(--text-tertiary);background:var(--bg-secondary);padding:10px 14px;border-radius:6px;border:1px solid var(--border)\">" +
                $"No incoming W3C traceparent attached at enqueue time. Execution activity recorded locally." +
                $"</div>";
        }

        var inputTypeShort = string.IsNullOrEmpty(job.InputType) ? "None (Void / Parameterless)" : Helpers.ShortType(job.InputType);

        var overviewCards =
            $"<div style=\"display:grid;grid-template-columns:repeat(auto-fit, minmax(min(360px, 100%), 1fr));gap:20px;margin-bottom:28px\">" +

            // Card 1: Configuration & Execution Policies
            $"<div class=\"card\" style=\"margin-bottom:0\">" +
            $"<div class=\"card-header\" style=\"display:flex;align-items:center;justify-content:space-between\">" +
            $"<h3 style=\"margin:0;font-size:14px;font-weight:600;display:flex;align-items:center;gap:8px\">" +
            $"<svg width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><rect x=\"2\" y=\"2\" width=\"20\" height=\"8\" rx=\"2\" ry=\"2\"/><rect x=\"2\" y=\"14\" width=\"20\" height=\"8\" rx=\"2\" ry=\"2\"/><line x1=\"6\" y1=\"6\" x2=\"6.01\" y2=\"6\"/><line x1=\"6\" y1=\"18\" x2=\"6.01\" y2=\"18\"/></svg>" +
            $"Configuration &amp; Governance</h3>" +
            $"<span class=\"badge\" style=\"background:var(--bg-tertiary);color:var(--text-secondary);font-size:11px\">Policy Spec</span>" +
            $"</div>" +
            $"<div style=\"padding:18px 20px;display:flex;flex-direction:column;gap:16px\">" +

            // Destination Queue & Worker Health hero
            $"<div>" +
            $"<div style=\"padding:12px 14px;background:var(--bg-secondary);border:1px solid var(--border);border-radius:8px;display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap\">" +
            $"<div>" +
            $"<div style=\"font-size:11px;color:var(--text-tertiary);text-transform:uppercase;font-weight:700\">Queue Destination</div>" +
            $"<div style=\"font-family:monospace;font-size:15px;font-weight:700;color:var(--primary);margin-top:2px\">{HttpUtility.HtmlEncode(job.Queue)}</div>" +
            $"</div>" +
            $"{workerIndicator}" +
            $"</div>" +
            $"</div>" +

            // Priority & Retry Budget Visualizer
            $"<div style=\"border-top:1px solid var(--border);padding-top:12px\">" +
            $"<div style=\"display:flex;align-items:center;justify-content:space-between;margin-bottom:8px\">" +
            $"<span style=\"font-size:11px;color:var(--text-tertiary);text-transform:uppercase;font-weight:700\">Priority &amp; Retry Budget</span>" +
            $"{priorityBadge}" +
            $"</div>" +
            $"<div style=\"display:flex;gap:4px;align-items:center;margin-bottom:6px\">{budgetSegments}</div>" +
            $"<div style=\"display:flex;justify-content:space-between;align-items:center;font-size:12px;color:var(--text-secondary)\">" +
            $"<span>Attempt <strong style=\"color:var(--text-primary)\">{job.Attempts}</strong> of {effectiveMax}</span>" +
            $"{budgetLabel}" +
            $"</div>" +
            $"</div>" +

            // Idempotency Deduplication Guard
            $"<div style=\"border-top:1px solid var(--border);padding-top:12px\">" +
            idempotencyHtml +
            $"</div>" +

            // Tags & Categorization
            $"<div style=\"border-top:1px solid var(--border);padding-top:12px\">" +
            $"<div style=\"font-size:11px;color:var(--text-tertiary);text-transform:uppercase;font-weight:700;margin-bottom:6px\">Tags &amp; Classification</div>" +
            $"<div>{tagsHtml}</div>" +
            $"</div>" +

            $"</div></div>" +

            // Card 2: Causality, Lineage & Telemetry
            $"<div class=\"card\" style=\"margin-bottom:0\">" +
            $"<div class=\"card-header\" style=\"display:flex;align-items:center;justify-content:space-between\">" +
            $"<h3 style=\"margin:0;font-size:14px;font-weight:600;display:flex;align-items:center;gap:8px\">" +
            $"<svg width=\"16\" height=\"16\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\"><path d=\"M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71\"/><path d=\"M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71\"/></svg>" +
            $"Lineage &amp; Observability</h3>" +
            $"<span class=\"badge\" style=\"background:var(--bg-tertiary);color:var(--text-secondary);font-size:11px\">OTel &amp; Lineage</span>" +
            $"</div>" +
            $"<div style=\"padding:18px 20px;display:flex;flex-direction:column;gap:16px\">" +

            // Origin / Provenance
            $"<div>" +
            originHtml +
            $"</div>" +

            // Distributed Tracing
            $"<div style=\"border-top:1px solid var(--border);padding-top:12px\">" +
            telemetryHtml +
            $"</div>" +

            // Type & Contract Spec
            $"<div style=\"border-top:1px solid var(--border);padding-top:12px\">" +
            $"<div style=\"display:flex;align-items:center;justify-content:space-between;margin-bottom:6px\">" +
            $"<span style=\"font-size:11px;color:var(--text-tertiary);text-transform:uppercase;font-weight:700\">Payload &amp; Type Contract</span>" +
            $"<span class=\"badge\" style=\"background:var(--bg-tertiary);border:1px solid var(--border);font-size:10px\">Schema v{job.SchemaVersion}</span>" +
            $"</div>" +
            $"<div style=\"font-size:12px;color:var(--text-primary)\">" +
            $"<div style=\"display:flex;align-items:center;justify-content:space-between;background:var(--bg-secondary);padding:6px 10px;border-radius:6px;border:1px solid var(--border);margin-bottom:4px;font-family:monospace;font-size:11px\">" +
            $"<span>ID: {job.Id.Value}</span>" +
            $"<button class=\"btn btn-secondary btn-sm\" onclick=\"navigator.clipboard.writeText('{job.Id.Value}');this.textContent='Copied!';setTimeout(()=>this.textContent='Copy',1200)\" title=\"Copy Job ID\" style=\"font-size:10px;padding:2px 6px\">Copy</button>" +
            $"</div>" +
            $"<div style=\"font-family:monospace;word-break:break-all;background:var(--bg-secondary);padding:6px 10px;border-radius:6px;border:1px solid var(--border);margin-bottom:4px\" title=\"{HttpUtility.HtmlAttributeEncode(job.JobType)}\">Job: {HttpUtility.HtmlEncode(Helpers.ShortType(job.JobType))}</div>" +
            $"<div style=\"font-family:monospace;word-break:break-all;background:var(--bg-secondary);padding:6px 10px;border-radius:6px;border:1px solid var(--border)\" title=\"{HttpUtility.HtmlAttributeEncode(job.InputType)}\">Input: {HttpUtility.HtmlEncode(inputTypeShort)}</div>" +
            $"</div>" +
            $"</div>" +

            $"</div></div>" +

            $"</div>";

        // Payload
        var payloadStrippedNotice = (job.Status == JobStatus.Succeeded && string.IsNullOrEmpty(job.InputJson))
            ? "<div class=\"alert alert-info\" style=\"margin-top:8px;font-size:12px\">ℹ️ Payload stripped by retention policy (TrimPayloadOnSuccess)</div>"
            : string.Empty;

        var payloadSection =
            "<div style=\"margin-bottom:28px\">" +
            "<div style=\"display:flex;justify-content:space-between;align-items:center;margin-bottom:8px\">" +
            "<h3 style=\"font-size:14px;font-weight:600;margin:0\">Payload</h3>" +
            "</div>" +
            "<div class=\"terminal-window\">" +
            "<div class=\"terminal-header\"><div class=\"terminal-dots\"><span></span><span></span><span></span></div><span class=\"terminal-title\">payload.json</span><button type=\"button\" class=\"copy-btn\" onclick=\"nexJobCopyCode(this)\" title=\"Copy pure JSON payload\">📋 Copy JSON</button></div>" +
            $"<div class=\"terminal-body\"><pre style=\"margin:0;font-size:12px;color:var(--terminal-text);overflow-x:auto;font-family:monospace;white-space:pre-wrap\">{Helpers.FormatJson(job.InputJson)}</pre></div>" +
            "</div>" +
            payloadStrippedNotice +
            "</div>";

        // Checkpoint state section (for long-running/batch checkpoints)
        var checkpointSection = string.Empty;
        if (!string.IsNullOrWhiteSpace(job.CheckpointJson))
        {
            checkpointSection =
                "<div style=\"margin-bottom:28px\">" +
                "<details class=\"card\" open style=\"padding:0;overflow:hidden\">" +
                "<summary style=\"padding:14px 18px;font-weight:600;font-size:14px;cursor:pointer;background:var(--bg-secondary);display:flex;align-items:center;gap:8px\">💾 Checkpoint State <span style=\"font-size:12px;font-weight:400;color:var(--text-tertiary)\">(Progress snapshot)</span></summary>" +
                "<div style=\"padding:16px\">" +
                "<div class=\"terminal-window\">" +
                "<div class=\"terminal-header\"><div class=\"terminal-dots\"><span></span><span></span><span></span></div><span class=\"terminal-title\">checkpoint.json</span><button type=\"button\" class=\"copy-btn\" onclick=\"nexJobCopyCode(this)\" title=\"Copy checkpoint state\">📋 Copy JSON</button></div>" +
                $"<div class=\"terminal-body\"><pre style=\"margin:0;font-size:12px;color:var(--terminal-text);overflow-x:auto;font-family:monospace;white-space:pre-wrap\">{Helpers.FormatJson(job.CheckpointJson)}</pre></div>" +
                "</div>" +
                "</div>" +
                "</details>" +
                "</div>";
        }

        // Error section
        var errorSection = HtmlFragments.ErrorSection(job.LastErrorMessage, job.LastErrorStackTrace);

        // Logs section
        var logsSection =
            $"<div style=\"margin-bottom:24px\">" +
            $"<div style=\"display:flex;justify-content:space-between;align-items:center;margin-bottom:8px\">" +
            $"<h3 style=\"font-size:14px;font-weight:600;margin:0\">Execution Logs <span id=\"logs-count-badge\" style=\"font-weight:400;color:var(--text-tertiary)\">({job.ExecutionLogs.Count} entries)</span></h3>" +
            (job.Status == JobStatus.Processing ? "<span class=\"badge badge-warning\"><span class=\"pulse-live\"></span> STREAMING LIVE</span>" : string.Empty) +
            $"</div>" +
            $"<div class=\"terminal-window\">" +
            $"<div class=\"terminal-header\"><div class=\"terminal-dots\"><span></span><span></span><span></span></div><span class=\"terminal-title\">job-{job.Id.Value.ToString()[..8]}.log</span><button type=\"button\" class=\"copy-btn\" onclick=\"nexJobCopyCode(this)\" title=\"Copy plain text logs\">📋 Copy Logs</button></div>" +
            $"<div id=\"terminal-logs-body\" class=\"terminal-body\" style=\"max-height:360px;overflow-y:auto;padding:12px 16px;font-family:monospace;font-size:12px\">" +
            (job.ExecutionLogs.Count > 0
                ? string.Join(string.Empty, job.ExecutionLogs.Select(entry =>
                {
                    var color = entry.Level switch
                    {
                        "Warning" => "var(--warning)",
                        "Error" or "Critical" => "var(--error)",
                        "Debug" or "Trace" => "var(--text-tertiary)",
                        _ => "var(--text-secondary)",
                    };
                    var ts = entry.Timestamp.ToString("HH:mm:ss.fff");
                    var msg = HttpUtility.HtmlEncode(entry.Message).Replace("\n", "&#10;");
                    return $"<div style=\"display:flex;gap:10px;line-height:1.6\"><span style=\"color:var(--terminal-subtext);flex-shrink:0\">[{ts}]</span><span style=\"color:{color};font-weight:600;min-width:70px;flex-shrink:0\">[{entry.Level}]</span><span style=\"color:var(--terminal-text);word-break:break-all\">{msg}</span></div>";
                }))
                : "<p style=\"color:var(--text-tertiary);margin:0\">No logs captured for this execution.</p>") +
            $"</div>" +
            $"</div>" +
            $"</div>";

        // SSE for live progress and log streaming
        var sseScript =
            $"<script>(function(){{" +
            $"var jobId='{job.Id.Value}';" +
            $"var fill=document.getElementById('progress-bar-fill');" +
            $"var pctEl=document.getElementById('progress-pct');" +
            $"var msgEl=document.getElementById('progress-msg');" +
            $"var logsBody=document.getElementById('terminal-logs-body');" +
            $"var logsBadge=document.getElementById('logs-count-badge');" +
            $"var isRunning={(job.Status == JobStatus.Processing ? "true" : "false")};" +
            $"var lastLogsCount={job.ExecutionLogs.Count};" +
            $"var es=new EventSource('{PathPrefix}/stream{clusterSuffix}');" +
            $"es.onmessage=function(e){{" +
            $"var d=JSON.parse(e.data);" +
            $"var jobs=d.activeJobs||[];" +
            $"var j=jobs.find(function(x){{return x.id===jobId;}});" +
            $"if(j&&j.progressPercent!==null&&j.progressPercent!==undefined&&fill){{" +
            $"fill.style.width=j.progressPercent+'%';" +
            $"if(pctEl)pctEl.textContent=j.progressPercent+'%';" +
            $"if(msgEl&&j.progressMessage)msgEl.textContent=j.progressMessage;" +
            $"}}" +
            $"if(isRunning){{" +
            $"fetch('{PathPrefix}/jobs/'+jobId+'/logs{clusterSuffix}').then(r=>r.json()).then(logs=>{{" +
            $"if(Array.isArray(logs)&&logs.length>lastLogsCount&&logsBody){{" +
            $"lastLogsCount=logs.length;" +
            $"if(logsBadge)logsBadge.textContent='('+logs.length+' entries)';" +
            $"logsBody.innerHTML=logs.map(l=>{{" +
            $"var c=l.level==='Warning'?'var(--warning)':(l.level==='Error'||l.level==='Critical'?'var(--error)':'var(--text-secondary)');" +
            $"var t=l.timestamp?l.timestamp.split(' ')[1]||l.timestamp:'';" +
            $"return '<div style=\"display:flex;gap:10px;line-height:1.6\"><span style=\"color:var(--terminal-subtext);flex-shrink:0\">['+t+']</span><span style=\"color:'+c+';font-weight:600;min-width:70px;flex-shrink:0\">['+l.level+']</span><span style=\"color:var(--terminal-text);word-break:break-all\">'+l.message+'</span></div>';" +
            $"}}).join('');" +
            $"logsBody.scrollTop=logsBody.scrollHeight;" +
            $"}}" +
            $"}}).catch(()=>{{}});" +
            $"}}" +
            $"}};es.onerror=function(){{es.close();}};" +
            $"}})();</script>";

        var body =
            (IsReadOnly ? HtmlFragments.ReadOnlyBanner() : string.Empty) +
            header +
            progressSection +
            "<div class=\"timeline-section\">" +
            executionTimeline +
            "</div>" +
            overviewCards +
            payloadSection +
            checkpointSection +
            errorSection +
            logsSection +
            sseScript;

        return HtmlShell.Wrap(Title, PathPrefix, "jobs", body, Counters, clusters: Clusters, activeCluster: ActiveCluster);
    }
}
