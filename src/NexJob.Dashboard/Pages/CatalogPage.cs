using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

[ExcludeFromCodeCoverage]
internal sealed class CatalogPage : IComponent
{
    private RenderHandle _handle;

    [Parameter] public IDashboardStorage Storage { get; set; } = default!;
    [Parameter] public string PathPrefix { get; set; } = "/dashboard";
    [Parameter] public string Title { get; set; } = "NexJob";
    [Parameter] public NavCounters? Counters { get; set; }
    [Parameter] public string? Search { get; set; }
    [Parameter] public string? QueueFilter { get; set; }
    [Parameter] public IReadOnlyList<DashboardCluster>? Clusters { get; set; }
    [Parameter] public DashboardCluster? ActiveCluster { get; set; }

    void IComponent.Attach(RenderHandle renderHandle) => _handle = renderHandle;

    async Task IComponent.SetParametersAsync(ParameterView parameters)
    {
        parameters.SetParameterProperties(this);

        // NOTE (Blazor Dispatcher Invariant):
        // Do NOT use .ConfigureAwait(false) here. Rendering via _handle.Render requires execution on the Dispatcher.
        var catalog = await Storage.GetJobCatalogAsync(CancellationToken.None);

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            catalog = catalog
                .Where(c => c.JobType.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                            Helpers.ShortType(c.JobType).Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(QueueFilter))
        {
            catalog = catalog
                .Where(c => string.Equals(c.Queue, QueueFilter.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        _handle.Render(b => b.AddMarkupContent(0, BuildHtml(catalog)));
    }

    private string BuildHtml(IReadOnlyList<JobCatalogItem> items)
    {
        var now = DateTimeOffset.UtcNow;
        var clusterQuery = ActiveCluster is not null ? $"?cluster={Uri.EscapeDataString(ActiveCluster.Id)}" : string.Empty;
        var clusterParam = ActiveCluster is not null ? $"&cluster={Uri.EscapeDataString(ActiveCluster.Id)}" : string.Empty;
        var isReadOnly = ActiveCluster?.IsReadOnly == true;

        if (items.Count == 0 && string.IsNullOrWhiteSpace(Search) && string.IsNullOrWhiteSpace(QueueFilter))
        {
            var emptyBody =
                HtmlFragments.Breadcrumbs(PathPrefix, ("Catalog", null)) +
                HtmlFragments.PageHeader("Job Catalog & Definitions", "Registered job types and execution telemetry") +
                HtmlFragments.EmptyState("0 0 24 24", "No job definitions found in storage.");
            return HtmlShell.Wrap(Title, PathPrefix, "catalog", emptyBody, Counters, clusters: Clusters, activeCluster: ActiveCluster);
        }

        var filterBar = BuildFilterBar();

        var rows = string.Join(string.Empty, items.Select(item =>
        {
            var shortName = Helpers.ShortType(item.JobType);
            var failureRate = item.TotalRuns > 0 ? (double)item.FailedRuns / item.TotalRuns * 100.0 : 0.0;
            var failureRateBadge = failureRate switch
            {
                > 10.0 => $"<span class=\"badge badge-error\">{failureRate:F1}%</span>",
                > 0.0 => $"<span class=\"badge badge-warning\">{failureRate:F1}%</span>",
                _ => "<span class=\"badge badge-success\">0.0%</span>",
            };

            string avgDurationText;
            if (!item.AvgDurationSeconds.HasValue)
            {
                avgDurationText = "—";
            }
            else if (item.AvgDurationSeconds.Value < 1.0)
            {
                var ms = item.AvgDurationSeconds.Value * 1000;
                avgDurationText = $"~{ms:F0}ms";
            }
            else
            {
                avgDurationText = $"~{item.AvgDurationSeconds.Value:F1}s";
            }

            var lastRunText = item.LastExecutedAt.HasValue
                ? Helpers.RelativeTime(item.LastExecutedAt.Value, now)
                : "Never";

            var historyUrl = $"{PathPrefix}/jobs?search={Uri.EscapeDataString(shortName)}&queue={Uri.EscapeDataString(item.Queue)}{clusterParam}";

            var isParameterless = Helpers.IsParameterlessJob(item.JobType);
            var inputType = Helpers.ResolveJobInputType(item.JobType);
            var sampleJson = inputType is not null ? Helpers.GenerateDefaultJsonSchema(inputType) : "{}";
            var inputTypeName = inputType is not null ? inputType.Name : "NoInput";

            string triggerAction;
            if (isReadOnly)
            {
                triggerAction = string.Empty;
            }
            else if (isParameterless)
            {
                triggerAction =
                    $"<form method=\"post\" action=\"{PathPrefix}/catalog/{Uri.EscapeDataString(item.JobType)}/trigger{clusterQuery}\" style=\"display:inline;margin:0\">" +
                    $"<input type=\"hidden\" name=\"queue\" value=\"{HttpUtility.HtmlAttributeEncode(item.Queue)}\" />" +
                    "<button type=\"submit\" class=\"btn btn-primary btn-sm\" style=\"min-width:76px;justify-content:center\" title=\"Enqueue ad-hoc execution (parameterless IJob)\">Trigger</button>" +
                    "</form>";
            }
            else
            {
                var encodedJobType = HttpUtility.JavaScriptStringEncode(item.JobType);
                var encodedShortName = HttpUtility.JavaScriptStringEncode(shortName);
                var encodedQueue = HttpUtility.JavaScriptStringEncode(item.Queue);
                var encodedInputType = HttpUtility.JavaScriptStringEncode(inputTypeName);
                var encodedSample = HttpUtility.JavaScriptStringEncode(sampleJson);

                triggerAction =
                    $"<button type=\"button\" class=\"btn btn-primary btn-sm\" style=\"min-width:76px;justify-content:center\" " +
                    $"onclick=\"openTriggerModal('{encodedJobType}', '{encodedShortName}', '{encodedQueue}', '{encodedInputType}', '{encodedSample}')\" " +
                    "title=\"Trigger execution with custom input payload (IJob&lt;T&gt;)\">Trigger</button>";
            }

            var actionsHtml =
                $"<div style=\"display:flex;gap:8px;justify-content:flex-end;align-items:center\">" +
                $"<a href=\"{historyUrl}\" class=\"btn btn-secondary btn-sm\" style=\"min-width:68px;justify-content:center\" title=\"View execution history in Jobs log\">History</a>" +
                triggerAction +
                "</div>";

            return $"""
                <tr>
                    <td>
                        <div style="font-weight:600;color:var(--text-primary)">{WebUtility.HtmlEncode(shortName)}</div>
                        <div style="font-family:monospace;font-size:11px;color:var(--text-secondary);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:280px" title="{HttpUtility.HtmlAttributeEncode(item.JobType)}">
                            {WebUtility.HtmlEncode(item.JobType)}
                        </div>
                    </td>
                    <td><span class="badge badge-gray">{WebUtility.HtmlEncode(item.Queue)}</span></td>
                    <td style="font-weight:600">{item.TotalRuns.ToString("N0", CultureInfo.InvariantCulture)}</td>
                    <td style="color:var(--success);font-weight:500">{item.SucceededRuns.ToString("N0", CultureInfo.InvariantCulture)}</td>
                    <td style="color:var(--error);font-weight:500">{item.FailedRuns.ToString("N0", CultureInfo.InvariantCulture)}</td>
                    <td>{failureRateBadge}</td>
                    <td style="color:var(--text-secondary)">{avgDurationText}</td>
                    <td style="font-size:12px;color:var(--text-secondary)">{lastRunText}</td>
                    <td style="text-align:right">{actionsHtml}</td>
                </tr>
                """;
        }));

        var modalHtml =
            """
            <style>
                #triggerModal{position:fixed;top:50%;left:50%;transform:translate(-50%,-50%);margin:0}
                #triggerModal::backdrop{background:rgba(0,0,0,.65)}
            </style>
            <dialog id="triggerModal" style="background:#111827;color:#e5e7eb;border:1px solid #374151;border-radius:8px;padding:0;max-width:650px;width:90vw;box-shadow:0 20px 25px -5px rgba(0,0,0,0.5)">
                <form id="triggerModalForm" method="post" action="">
                    <div style="display:flex;justify-content:space-between;align-items:center;padding:16px 20px;border-bottom:1px solid #374151">
                        <div>
                            <h3 id="triggerModalTitle" style="margin:0;font-size:16px;color:#f9fafb;font-weight:600">Trigger Job</h3>
                            <div id="triggerModalSubtitle" style="font-size:12px;color:#9ca3af;margin-top:2px">Configure input payload and target queue</div>
                        </div>
                        <button type="button" onclick="document.getElementById('triggerModal').close()" style="background:none;border:none;color:#9ca3af;cursor:pointer;font-size:20px;line-height:1">&#x00D7;</button>
                    </div>
                    <div style="padding:20px;display:flex;flex-direction:column;gap:16px">
                        <div>
                            <label style="display:block;font-size:12px;font-weight:600;color:#9ca3af;margin-bottom:6px;text-transform:uppercase;letter-spacing:0.5px">Queue</label>
                            <input type="text" id="triggerModalQueue" name="queue" style="width:100%;box-sizing:border-box" required />
                        </div>
                        <div>
                            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:6px">
                                <label style="font-size:12px;font-weight:600;color:#9ca3af;text-transform:uppercase;letter-spacing:0.5px">Payload (<span id="triggerModalInputType">JSON</span>)</label>
                                <button type="button" id="triggerModalResetBtn" class="btn btn-secondary btn-sm" style="font-size:11px;padding:2px 8px">Reset Sample</button>
                            </div>
                            <textarea id="triggerModalPayload" name="inputJson" rows="8" style="width:100%;box-sizing:border-box;font-family:monospace;font-size:13px;background:#0f172a;color:#e2e8f0;border:1px solid #374151;border-radius:6px;padding:12px;line-height:1.5;resize:vertical"></textarea>
                        </div>
                    </div>
                    <div style="padding:14px 20px;border-top:1px solid #374151;display:flex;justify-content:flex-end;gap:10px;background:rgba(0,0,0,0.15)">
                        <button type="button" class="btn btn-secondary btn-sm" onclick="document.getElementById('triggerModal').close()">Cancel</button>
                        <button type="submit" class="btn btn-primary btn-sm">Enqueue Job</button>
                    </div>
                </form>
            </dialog>
            """;

        var scriptHtml =
            $$"""
            <script>
                var defaultSampleJson = '{}';
                function openTriggerModal(jobType, shortName, queue, inputType, sampleJson) {
                    var modal = document.getElementById('triggerModal');
                    var form = document.getElementById('triggerModalForm');
                    var title = document.getElementById('triggerModalTitle');
                    var queueInput = document.getElementById('triggerModalQueue');
                    var payloadInput = document.getElementById('triggerModalPayload');
                    var inputTypeSpan = document.getElementById('triggerModalInputType');

                    defaultSampleJson = sampleJson;
                    form.action = '{{PathPrefix}}/catalog/' + encodeURIComponent(jobType) + '/trigger{{clusterQuery}}';
                    title.textContent = 'Trigger ' + shortName;
                    queueInput.value = queue;
                    payloadInput.value = sampleJson;
                    inputTypeSpan.textContent = inputType;

                    modal.showModal();
                }

                document.getElementById('triggerModalResetBtn').addEventListener('click', function() {
                    document.getElementById('triggerModalPayload').value = defaultSampleJson;
                });
            </script>
            """;

        var body =
            "<div id=\"catalog-page-content\" data-refresh=\"true\">" +
            (isReadOnly ? HtmlFragments.ReadOnlyBanner() : string.Empty) +
            HtmlFragments.Breadcrumbs(PathPrefix, ("Catalog", null)) +
            HtmlFragments.PageHeader("Job Catalog & Definitions", "Registered job types, execution counts, error rates, and ad-hoc triggering") +
            filterBar +
            "<div class=\"card\">" +
            $"<div class=\"card-header\"><h3>{items.Count} definition{(items.Count == 1 ? string.Empty : "s")} found</h3></div>" +
            "<div class=\"table-container\">" +
            "<table class=\"table\">" +
            "<thead><tr>" +
            "<th>Job Type</th>" +
            "<th>Queue</th>" +
            "<th>Total Runs</th>" +
            "<th>Succeeded</th>" +
            "<th>Failed</th>" +
            "<th>Failure Rate</th>" +
            "<th>Avg Duration</th>" +
            "<th>Last Executed</th>" +
            "<th style=\"text-align:right\">Actions</th>" +
            "</tr></thead>" +
            $"<tbody>{rows}</tbody>" +
            "</table>" +
            "</div>" +
            "</div>" +
            modalHtml +
            scriptHtml +
            "</div>";

        return HtmlShell.Wrap(Title, PathPrefix, "catalog", body, Counters, clusters: Clusters, activeCluster: ActiveCluster);
    }

    private string BuildFilterBar()
    {
        var clusterParam = ActiveCluster is not null ? $"<input type=\"hidden\" name=\"cluster\" value=\"{HttpUtility.HtmlAttributeEncode(ActiveCluster.Id)}\" />" : string.Empty;
        var searchVal = HttpUtility.HtmlAttributeEncode(Search ?? string.Empty);
        var queueVal = HttpUtility.HtmlAttributeEncode(QueueFilter ?? string.Empty);

        return $"""
            <div class="card" style="padding:16px 20px;margin-bottom:20px">
                <form method="get" action="{PathPrefix}/catalog" style="display:flex;gap:12px;align-items:center;flex-wrap:wrap">
                    {clusterParam}
                    <div style="flex:1;min-width:200px">
                        <input type="text" name="search" value="{searchVal}" placeholder="Filter by Job Type..." style="width:100%" />
                    </div>
                    <div style="min-width:140px">
                        <input type="text" name="queue" value="{queueVal}" placeholder="Filter by Queue..." style="width:100%" />
                    </div>
                    <button type="submit" class="btn btn-primary btn-sm">Filter</button>
                    <a href="{PathPrefix}/catalog{(ActiveCluster is not null ? $"?cluster={Uri.EscapeDataString(ActiveCluster.Id)}" : string.Empty)}" class="btn btn-secondary btn-sm">Reset</a>
                </form>
            </div>
            """;
    }
}
