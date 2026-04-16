using System.Diagnostics.CodeAnalysis;
using NexJob.Storage;

namespace NexJob.Dashboard;

/// <summary>Shared HTML shell (layout wrapper) injected around page component output.</summary>
[ExcludeFromCodeCoverage]
internal static class HtmlShell
{
    private const string Css =
        """
        :root {
            --primary: #00cfd5;
            --primary-dark: #00b8bc;
            --primary-light: rgba(0, 207, 213, 0.12);
            --secondary: #7367f0;
            --success: #28c76f;
            --success-light: rgba(40, 199, 111, 0.12);
            --warning: #ff9f43;
            --warning-light: rgba(255, 159, 67, 0.12);
            --error: #ea5455;
            --error-light: rgba(234, 84, 85, 0.12);
            --info: #00cfe8;
            --info-light: rgba(0, 207, 213, 232, 0.12);
            --bg-primary: #ffffff;
            --bg-secondary: #f8f7fa;
            --bg-tertiary: #f1f0f5;
            --text-primary: #2f2b3d;
            --text-secondary: #6f6b7d;
            --text-tertiary: #b0adba;
            --border: #dbdade;
            --radius: 12px;
            --transition: all 0.2s ease-in-out;
            --shadow: 0 4px 18px 0 rgba(75, 70, 92, 0.1);
        }
        [data-theme="dark"] {
            --bg-primary: #2f3349;
            --bg-secondary: #25293c;
            --bg-tertiary: #161924;
            --text-primary: #cfd3db;
            --text-secondary: #a5a3ae;
            --text-tertiary: #7983bb;
            --border: #43495e;
            --border-light: #43495e;
            --shadow: 0 4px 18px 0 rgba(15, 20, 34, 0.4);
        }
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: 'Public Sans', -apple-system, sans-serif;
            background: var(--bg-secondary); color: var(--text-primary);
            line-height: 1.5; -webkit-font-smoothing: antialiased;
        }
        .container { display: flex; min-height: 100vh; }
        .sidebar {
            width: 260px; background: var(--bg-primary);
            border-right: 1px solid var(--border);
            display: flex; flex-direction: column;
            position: fixed; height: 100vh; z-index: 100;
            box-shadow: var(--shadow);
        }
        [data-theme="dark"] .sidebar { background: #2f3349; }
        .logo { padding: 24px; display: flex; align-items: center; justify-content: space-between; gap: 12px; }
        .logo h1 { font-size: 22px; font-weight: 700; color: var(--text-primary); letter-spacing: -0.5px; }
        
        .theme-toggle { cursor: pointer; color: var(--text-tertiary); padding: 4px; border-radius: 4px; }
        .theme-toggle:hover { color: var(--primary); background: var(--bg-tertiary); }

        .nav { flex: 1; padding: 0 12px; display: flex; flex-direction: column; gap: 4px; overflow-y: auto; }
        .nav-item {
            display: flex; align-items: center; justify-content: space-between;
            padding: 10px 16px; border-radius: 6px;
            color: var(--text-secondary); text-decoration: none;
            font-size: 15px; transition: var(--transition);
        }
        .nav-item:hover { background: var(--bg-tertiary); transform: translateX(5px); }
        .nav-item.active {
            background: linear-gradient(72.47deg, var(--primary) 22.16%, rgba(0, 207, 213, 0.7) 76.47%);
            color: #fff; box-shadow: 0px 2px 6px rgba(0, 207, 213, 0.48);
        }
        .nav-counter { font-size: 11px; font-weight: 600; padding: 2px 6px; border-radius: 10px; background: rgba(0,0,0,0.1); color: inherit; }
        .nav-counter.alert { background: var(--error); color: #fff; }

        .main-content { flex: 1; margin-left: 260px; padding: 32px; }
        .page-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 24px; }
        .page-title { font-size: 24px; font-weight: 700; color: var(--text-primary); margin-bottom: 4px; }
        .page-subtitle { font-size: 14px; color: var(--text-secondary); }

        .stats-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 24px; margin-bottom: 32px; }
        .stat-card {
            background: var(--bg-primary); border-radius: var(--radius); padding: 20px;
            box-shadow: var(--shadow); transition: var(--transition);
            border: 1px solid transparent; display: flex; gap: 16px; align-items: center;
        }
        .stat-card:hover { border-color: var(--primary); transform: translateY(-5px); }
        .stat-icon {
            width: 44px; height: 44px; border-radius: 8px;
            display: flex; align-items: center; justify-content: center; flex-shrink: 0;
        }
        .stat-icon-success { background: var(--success-light); color: var(--success); }
        .stat-icon-warning { background: var(--warning-light); color: var(--warning); }
        .stat-icon-error   { background: var(--error-light);   color: var(--error); }
        .stat-icon-info    { background: var(--info-light);    color: var(--info); }
        
        .stat-value { font-size: 26px; font-weight: 700; color: var(--text-primary); line-height: 1.2; }
        .stat-label { font-size: 14px; color: var(--text-secondary); font-weight: 500; }
        .stat-sublabel { font-size: 11px; color: var(--text-tertiary); }

        .card { background: var(--bg-primary); border-radius: var(--radius); box-shadow: var(--shadow); border: none; overflow: hidden; margin-bottom: 24px; }
        .card-header { padding: 16px 20px; border-bottom: 1px solid var(--border); display: flex; align-items: center; justify-content: space-between; }
        .card-header h3 { font-size: 16px; font-weight: 600; color: var(--text-primary); }

        .table { width: 100%; border-collapse: collapse; font-size: 14px; }
        .table th { padding: 12px 24px; text-align: left; font-size: 12px; font-weight: 600; color: var(--text-tertiary); text-transform: uppercase; letter-spacing: 0.5px; border-bottom: 1px solid var(--border); }
        .table td { padding: 12px 24px; border-bottom: 1px solid var(--border); color: var(--text-primary); vertical-align: middle; }
        .table tbody tr:last-child td { border-bottom: none; }
        .table tbody tr:hover { background: var(--bg-secondary); }

        .badge { padding: 4px 8px; border-radius: 4px; font-size: 11px; font-weight: 600; text-transform: uppercase; }
        .badge-success { background: var(--success-light); color: var(--success); }
        .badge-error   { background: var(--error-light);   color: var(--error); }
        .badge-warning { background: var(--warning-light); color: var(--warning); }
        .badge-info    { background: var(--info-light);    color: var(--info); }

        .dot { width: 8px; height: 8px; border-radius: 50%; display: inline-block; vertical-align: middle; margin-right: 8px; }
        .dot-processing { background: var(--warning); animation: pulse 2s infinite; }
        .dot-succeeded { background: var(--success); }
        .dot-failed { background: var(--error); }
        .dot-enqueued, .dot-scheduled { background: var(--info); }
        @keyframes pulse { 0% { opacity: 1; } 50% { opacity: 0.4; } 100% { opacity: 1; } }

        .job-row { 
            display: grid; grid-template-columns: 24px 32px 250px 150px 100px auto; 
            align-items: center; gap: 16px; padding: 10px 24px; 
            white-space: nowrap; overflow: hidden;
            border-bottom: 1px solid var(--border); transition: var(--transition); 
            font-size: 13px;
        }
        .job-row > * { overflow: hidden; text-overflow: ellipsis; }
        .job-row:hover { background: var(--bg-secondary); }
        .job-row-main { font-weight: 600; font-size: 14px; }
        .job-row-sub { font-size: 12px; color: var(--text-secondary); font-family: monospace; }

        .queue-card { background: var(--bg-primary); border-radius: var(--radius); padding: 16px 24px; box-shadow: var(--shadow); margin-bottom: 12px; border: 1px solid transparent; }
        .queue-card:hover { border-color: var(--primary); }

        .btn { padding: 8px 16px; border-radius: 6px; font-size: 13px; font-weight: 600; cursor: pointer; transition: var(--transition); border: none; display: inline-flex; align-items: center; gap: 8px; }
        .btn-primary { background: var(--primary); color: white; }
        .btn-primary:hover { background: var(--primary-dark); transform: translateY(-1px); box-shadow: 0 4px 12px rgba(0, 207, 213, 0.3); }
        .btn-primary:active { transform: translateY(0); }
        .btn-secondary { background: var(--bg-tertiary); color: var(--text-secondary); }
        .btn-secondary:hover { background: var(--border); color: var(--text-primary); }
        .btn-danger { background: var(--error-light); color: var(--error); }
        .btn-danger:hover { background: var(--error); color: white; }
        .btn-sm { padding: 4px 10px; font-size: 12px; }
        
        .btn-icon-sm { width: 32px; height: 32px; border-radius: 6px; display: inline-flex; align-items: center; justify-content: center; background: var(--bg-tertiary); color: var(--text-secondary); cursor: pointer; border: none; transition: var(--transition); }
        .btn-icon-sm:hover { background: var(--primary-light); color: var(--primary); transform: translateY(-1px); }

        input[type="text"], input[type="number"], select { background: var(--bg-tertiary); color: var(--text-primary); border: 1px solid var(--border); border-radius: 6px; padding: 8px 12px; font-size: 14px; outline: none; transition: var(--transition); }
        input[type="text"]:focus, input[type="number"]:focus, select:focus { border-color: var(--primary); box-shadow: 0 0 0 2px var(--primary-light); }

        .health-badge { padding: 6px 12px; border-radius: 8px; font-size: 11px; font-weight: 700; display: flex; align-items: center; gap: 8px; margin: 16px; }
        .health-badge.healthy { background: var(--success-light); color: var(--success); }
        .health-badge.incident { background: var(--error-light); color: var(--error); }
        .health-badge.degraded { background: var(--warning-light); color: var(--warning); }
        .health-pulse { width: 6px; height: 6px; border-radius: 50%; background: currentColor; animation: pulse 2s infinite; }
        
        .bulk-toolbar { position: fixed; bottom: 24px; left: 290px; right: 30px; background: var(--bg-primary); border: 1px solid var(--primary); border-radius: var(--radius); padding: 16px 24px; display: none; align-items: center; justify-content: space-between; box-shadow: 0 10px 40px rgba(0,0,0,0.3); z-index: 1000; }
        .worker-row { display: flex; align-items: center; gap: 16px; margin-bottom: 8px; }
        .worker-id { font-size: 11px; font-weight: 700; color: var(--text-tertiary); min-width: 32px; }
        .worker-track { flex: 1; height: 18px; background: var(--bg-tertiary); border-radius: 4px; overflow: hidden; position: relative; }
        .worker-fill { height: 100%; border-radius: 4px; display: flex; align-items: center; padding: 0 10px; transition: width 0.3s ease; }
        .worker-fill.busy { background: var(--primary); }
        .worker-fill.slow { background: var(--warning); }
        .worker-job-name { font-size: 10px; font-weight: 600; color: white; white-space: nowrap; }
        .worker-elapsed { font-size: 11px; font-weight: 600; color: var(--text-secondary); width: 60px; text-align: right; }
        .worker-warn { color: var(--error); width: 16px; text-align: center; }

        .breadcrumbs { margin-bottom: 24px; font-size: 14px; color: var(--primary); display: flex; align-items: center; gap: 8px; }
        .breadcrumbs a { color: var(--primary); text-decoration: none; }
        .breadcrumbs .separator { color: var(--text-tertiary); }
        .breadcrumbs .current { color: var(--text-secondary); font-weight: 500; }

        .sidebar-footer { margin-top: auto; padding: 16px 20px; border-top: 1px solid var(--border); display: flex; justify-content: space-between; align-items: center; font-size: 11px; color: var(--text-secondary); }
        .github-link { font-size: 11px; color: var(--text-secondary); text-decoration: none; transition: var(--transition); display: flex; align-items: center; gap: 4px; }
        .github-link:hover { color: var(--primary); }
        [data-theme="light"] .theme-icon-sun, [data-theme="dark"] .theme-icon-moon { display: none; }

        .chart { padding: 24px; background: var(--bg-secondary); border-radius: var(--radius); }
        .chart-header { margin-bottom: 24px; }
        .section-title { font-size: 16px; font-weight: 600; color: var(--text-primary); }
        .bars { display: flex; align-items: flex-end; gap: 6px; height: 120px; padding-bottom: 25px; border-bottom: 2px solid var(--border); position: relative; }
        .bar-wrap { flex: 1; display: flex; flex-direction: column; align-items: center; height: 100%; justify-content: flex-end; position: relative; }
        .bar { width: 100%; background: var(--primary) !important; border-radius: 4px 4px 0 0; transition: height 0.3s ease; cursor: pointer; min-height: 2px; }
        .bar:hover { filter: brightness(1.1); transform: scaleX(1.1); }
        .bar.anomaly { background: var(--error); }
        .bar-label { position: absolute; bottom: -22px; font-size: 10px; color: var(--text-tertiary); font-weight: 600; }
        .chart-tooltip { position: fixed; background: #2f3349; color: #fff; padding: 6px 12px; border-radius: 4px; font-size: 12px; pointer-events: none; display: none; z-index: 1000; box-shadow: 0 4px 12px rgba(0,0,0,0.2); border: 1px solid rgba(255,255,255,0.1); }
        .avg-line { position: absolute; left: 0; right: 0; border-top: 1px dashed var(--text-tertiary); opacity: 0.4; pointer-events: none; z-index: 1; }
        .anomaly-note { font-size: 12px; color: var(--error); margin-top: 16px; font-weight: 500; display: flex; align-items: center; gap: 6px; }
        """;

    /// <summary>Wraps the content in the standard HTML shell.</summary>
    internal static string Wrap(string title, string pathPrefix, string activeRoute, string body, NavCounters? counters = null, JobMetrics? metrics = null) =>
        $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" /><meta name="viewport" content="width=device-width, initial-scale=1" /><title>{{title}}</title>
            <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Public+Sans:wght@400;500;600;700&display=swap">
            <style>{{Css}}</style>
        </head>
        <body>
        <div class="container">
            <nav class="sidebar">
                <div class="logo">
                    <h1>NexJob</h1>
                </div>
                {{HealthBadge(metrics)}}
                <div class="nav">
                    <a href="{{pathPrefix}}" class="nav-item {{Active(activeRoute, "overview")}}">Overview</a>
                    <a href="{{pathPrefix}}/queues" class="nav-item {{Active(activeRoute, "queues")}}"><span>Queues</span> {{NavCounter(counters?.Queues, counters?.QueuesClass)}}</a>
                    <a href="{{pathPrefix}}/servers" class="nav-item {{Active(activeRoute, "servers")}}"><span>Servers</span> {{NavCounter(counters?.Servers, counters?.ServersClass)}}</a>
                    <a href="{{pathPrefix}}/jobs" class="nav-item {{Active(activeRoute, "jobs")}}"><span>Jobs</span> {{NavCounter(counters?.Jobs, null)}}</a>
                    <a href="{{pathPrefix}}/recurring" class="nav-item {{Active(activeRoute, "recurring")}}"><span>Recurring</span> {{NavCounter(counters?.Recurring, null)}}</a>
                    <a href="{{pathPrefix}}/failed" class="nav-item {{Active(activeRoute, "failed")}}"><span>Failed</span> {{NavCounter(counters?.Failed, counters?.FailedClass)}}</a>
                    <a href="{{pathPrefix}}/settings" class="nav-item {{Active(activeRoute, "settings")}}">Settings</a>
                </div>
                <div class="sidebar-footer">
                    <span>v3.0.0</span>
                    <div class="theme-toggle" onclick="nexJobToggleTheme()" title="Toggle Theme">
                        <svg class="theme-icon-sun" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="5"/><line x1="12" y1="1" x2="12" y2="3"/><line x1="12" y1="21" x2="12" y2="23"/><line x1="4.22" y1="4.22" x2="5.64" y2="5.64"/><line x1="18.36" y1="18.36" x2="19.78" y2="19.78"/><line x1="1" y1="12" x2="3" y2="12"/><line x1="21" y1="12" x2="23" y2="12"/><line x1="4.22" y1="19.78" x2="5.64" y2="18.36"/><line x1="18.36" y1="5.64" x2="19.78" y2="4.22"/></svg>
                        <svg class="theme-icon-moon" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/></svg>
                    </div>
                    <a href="https://github.com/oluciano/NexJob" target="_blank" class="github-link">GitHub ↗</a>
                </div>
            </nav>
            <main class="main-content">{{body}}</main>
            <div id="bulk-toolbar" class="bulk-toolbar">
                <div class="bulk-info"><span id="bulk-count">0</span> jobs selected</div>
                <div class="bulk-actions">
                    <button id="bulk-requeue-btn" class="btn btn-primary" onclick="nexJobBulkAction('requeue')">↺ Requeue</button>
                    <button id="bulk-delete-btn" class="btn btn-danger" onclick="nexJobBulkAction('delete')">Delete</button>
                    <button class="btn btn-secondary" onclick="nexJobClearSelection()">Cancel</button>
                </div>
            </div>
        </div>
        <script>
        (function(){
            var h=document.documentElement,t=localStorage.getItem('nexjob-theme')||'dark';
            h.setAttribute('data-theme',t);
            window.nexJobToggleTheme = function() {
                var current = document.documentElement.getAttribute('data-theme');
                var next = current === 'dark' ? 'light' : 'dark';
                document.documentElement.setAttribute('data-theme', next);
                localStorage.setItem('nexjob-theme', next);
            };
            window.nexJobUpdateSelection = function() {
                var checked = document.querySelectorAll('.job-check:checked');
                var bar = document.getElementById('bulk-toolbar');
                var count = document.getElementById('bulk-count');
                if (checked.length > 0) { count.textContent = checked.length; bar.style.display = 'flex'; }
                else { bar.style.display = 'none'; }
            };
            window.nexJobClearSelection = function() { document.querySelectorAll('.job-check:checked').forEach(c => c.checked = false); nexJobUpdateSelection(); };
            window.nexJobBulkAction = async function(action) {
                var ids = Array.from(document.querySelectorAll('.job-check:checked')).map(c => c.value);
                if (ids.length === 0) return;
                var res = await fetch('{{pathPrefix}}/jobs/bulk-' + action, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ids: ids }) });
                if (res.ok) location.reload();
            };
            async function nexJobPoll() {
                setTimeout(nexJobPoll, 5000);
                if (document.querySelectorAll('.job-check:checked').length > 0) return;
                var res = await fetch(window.location.href, { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
                if (res.ok) {
                    var doc = new DOMParser().parseFromString(await res.text(), 'text/html');
                    document.querySelectorAll('[data-refresh="true"]').forEach(el => {
                        var newEl = doc.getElementById(el.id);
                        if (newEl && el.innerHTML !== newEl.innerHTML) el.innerHTML = newEl.innerHTML;
                    });
                }
            }
            setTimeout(nexJobPoll, 5000);
        })();
        </script>
        </body>
        </html>
        """;

    /// <summary>Generates a 404 page.</summary>
    internal static string NotFound(string title, string pathPrefix) => Wrap(title, pathPrefix, string.Empty, "404 Not Found");

    private static string HealthBadge(JobMetrics? m)
    {
        if (m is null)
        {
            return "<div class=\"health-badge healthy\"><span class=\"health-pulse\"></span>HEALTHY</div>";
        }

        if (m.Failed > 0 && m.Processing == 0)
        {
            return "<div class=\"health-badge incident\"><span class=\"health-pulse\"></span>INCIDENT</div>";
        }

        if (m.Failed > 0)
        {
            return "<div class=\"health-badge degraded\"><span class=\"health-pulse\"></span>DEGRADED</div>";
        }

        return "<div class=\"health-badge healthy\"><span class=\"health-pulse\"></span>HEALTHY</div>";
    }

    private static string NavCounter(string? value, string? cls)
    {
        if (string.IsNullOrEmpty(value) || string.Equals(value, "0", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var clsPart = string.IsNullOrEmpty(cls) ? string.Empty : " " + cls;
        return $"<span class=\"nav-counter{clsPart}\">{value}</span>";
    }

    private static string Active(string route, string page) => string.Equals(route, page, StringComparison.Ordinal) ? "active" : string.Empty;
}
