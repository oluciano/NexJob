using NexJob.Storage;

namespace NexJob.Dashboard;

/// <summary>Shared HTML shell (layout wrapper) injected around page component output.</summary>
internal static class HtmlShell
{
    private const string Css =
        """
        :root {
            --primary: #6366f1;
            --primary-dark: #4f46e5;
            --primary-light: #818cf8;
            --secondary: #8b5cf6;
            --success: #10b981;
            --success-light: #d1fae5;
            --warning: #f59e0b;
            --warning-light: #fef3c7;
            --error: #ef4444;
            --error-light: #fee2e2;
            --info: #3b82f6;
            --info-light: #dbeafe;
            --bg-primary: #ffffff;
            --bg-secondary: #f9fafb;
            --bg-tertiary: #f3f4f6;
            --text-primary: #111827;
            --text-secondary: #6b7280;
            --text-tertiary: #9ca3af;
            --border: #e5e7eb;
            --border-light: #f3f4f6;
            --shadow-sm: 0 1px 2px 0 rgba(0,0,0,0.05);
            --shadow: 0 1px 3px 0 rgba(0,0,0,0.1), 0 1px 2px 0 rgba(0,0,0,0.06);
            --shadow-md: 0 4px 6px -1px rgba(0,0,0,0.1), 0 2px 4px -1px rgba(0,0,0,0.06);
            --shadow-lg: 0 10px 15px -3px rgba(0,0,0,0.1), 0 4px 6px -2px rgba(0,0,0,0.05);
            --radius-sm: 6px;
            --radius: 8px;
            --radius-lg: 12px;
            --transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
        }
        [data-theme="dark"] {
            --primary: #818cf8;
            --primary-dark: #6366f1;
            --primary-light: #a5b4fc;
            --secondary: #a78bfa;
            --success: #34d399;
            --success-light: #064e3b;
            --warning: #fbbf24;
            --warning-light: #78350f;
            --error: #f87171;
            --error-light: #7f1d1d;
            --info: #60a5fa;
            --info-light: #1e3a8a;
            --bg-primary: #1f2937;
            --bg-secondary: #111827;
            --bg-tertiary: #0f172a;
            --text-primary: #f9fafb;
            --text-secondary: #d1d5db;
            --text-tertiary: #9ca3af;
            --border: #374151;
            --border-light: #4b5563;
            --shadow-md: 0 4px 6px -1px rgba(0,0,0,0.4), 0 2px 4px -1px rgba(0,0,0,0.3);
            --shadow-lg: 0 10px 15px -3px rgba(0,0,0,0.5), 0 4px 6px -2px rgba(0,0,0,0.3);
        }
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            background: var(--bg-secondary); color: var(--text-primary);
            line-height: 1.6; -webkit-font-smoothing: antialiased;
        }
        .container { display: flex; min-height: 100vh; }
        .sidebar {
            width: 280px; background: var(--bg-primary);
            border-right: 1px solid var(--border);
            display: flex; flex-direction: column;
            position: fixed; height: 100vh; overflow-y: auto; z-index: 100;
        }
        .logo {
            display: flex; align-items: center; gap: 12px;
            padding: 24px 20px; border-bottom: 1px solid var(--border);
        }
        .logo h1 {
            font-size: 20px; font-weight: 700;
            background: linear-gradient(135deg, var(--primary), var(--secondary));
            -webkit-background-clip: text; -webkit-text-fill-color: transparent; background-clip: text;
        }
        .nav { flex: 1; padding: 16px 12px; display: flex; flex-direction: column; gap: 4px; }
        .nav-item {
            display: flex; align-items: center; gap: 12px;
            padding: 12px 16px; border-radius: var(--radius);
            color: var(--text-secondary); text-decoration: none;
            font-size: 14px; font-weight: 500; transition: var(--transition);
        }
        .nav-item:hover { background: var(--bg-secondary); color: var(--text-primary); text-decoration: none; }
        .nav-item.active {
            background: linear-gradient(135deg, var(--primary), var(--secondary));
            color: white; box-shadow: var(--shadow-md);
        }
        .nav-badge {
            margin-left: auto; background: var(--error); color: white;
            font-size: 11px; font-weight: 600; padding: 2px 7px; border-radius: 9999px;
        }
        .sidebar-footer {
            padding: 16px; border-top: 1px solid var(--border);
            display: flex; align-items: center; justify-content: space-between;
        }
        .version-info { font-size: 11px; color: var(--text-tertiary); }
        .theme-toggle {
            width: 36px; height: 36px; border-radius: var(--radius);
            border: 1px solid var(--border); background: var(--bg-secondary);
            color: var(--text-secondary);
            display: flex; align-items: center; justify-content: center;
            cursor: pointer; transition: var(--transition);
        }
        .theme-toggle:hover { background: var(--bg-tertiary); color: var(--text-primary); }
        .main-content { flex: 1; margin-left: 280px; padding: 32px; }
        .page-header {
            display: flex; justify-content: space-between;
            align-items: flex-start; margin-bottom: 32px; gap: 16px;
        }
        .page-header h2 { font-size: 28px; font-weight: 700; color: var(--text-primary); margin-bottom: 4px; }
        .page-subtitle { font-size: 14px; color: var(--text-secondary); }
        .page-actions { display: flex; gap: 8px; align-items: center; }
        .stats-grid {
            display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 20px; margin-bottom: 32px;
        }
        .stat-card {
            background: var(--bg-primary); border: 1px solid var(--border);
            border-radius: var(--radius-lg); padding: 24px;
            display: flex; gap: 16px; transition: var(--transition);
        }
        .stat-card:hover { box-shadow: var(--shadow-lg); transform: translateY(-2px); }
        .stat-icon {
            width: 48px; height: 48px; border-radius: var(--radius);
            display: flex; align-items: center; justify-content: center; flex-shrink: 0;
        }
        .stat-icon-success { background: var(--success-light); color: var(--success); }
        .stat-icon-warning { background: var(--warning-light); color: var(--warning); }
        .stat-icon-error   { background: var(--error-light);   color: var(--error); }
        .stat-icon-info    { background: var(--info-light);    color: var(--info); }
        .stat-icon-gray    { background: var(--bg-tertiary);   color: var(--text-secondary); }
        .stat-content { flex: 1; }
        .stat-value { font-size: 32px; font-weight: 700; color: var(--text-primary); line-height: 1; margin-bottom: 4px; }
        .stat-label { font-size: 14px; color: var(--text-secondary); margin-bottom: 4px; }
        .stat-sublabel { font-size: 12px; color: var(--text-tertiary); }
        .content-grid { display: grid; gap: 24px; }
        .card { background: var(--bg-primary); border: 1px solid var(--border); border-radius: var(--radius-lg); overflow: hidden; }
        .card-header {
            padding: 20px 24px; border-bottom: 1px solid var(--border);
            display: flex; justify-content: space-between; align-items: center;
        }
        .card-header h3 { font-size: 16px; font-weight: 600; color: var(--text-primary); }
        .btn {
            display: inline-flex; align-items: center; gap: 6px;
            padding: 8px 16px; border-radius: var(--radius);
            font-size: 14px; font-weight: 500; cursor: pointer;
            transition: var(--transition); border: 1px solid transparent; text-decoration: none;
        }
        .btn-primary { background: linear-gradient(135deg, var(--primary), var(--secondary)); color: white; border: none; }
        .btn-primary:hover { opacity: 0.9; transform: translateY(-1px); box-shadow: var(--shadow-md); }
        .btn-secondary { background: var(--bg-primary); color: var(--text-secondary); border-color: var(--border); }
        .btn-secondary:hover { background: var(--bg-secondary); color: var(--text-primary); }
        .btn-danger { background: var(--error); color: white; border: none; }
        .btn-danger:hover { opacity: 0.9; }
        .btn-icon-sm {
            width: 32px; height: 32px; border-radius: var(--radius-sm);
            border: 1px solid var(--border); background: transparent;
            color: var(--text-secondary);
            display: inline-flex; align-items: center; justify-content: center;
            cursor: pointer; transition: var(--transition);
        }
        .btn-icon-sm:hover { background: var(--bg-secondary); color: var(--text-primary); }
        .table-container { overflow-x: auto; }
        .table { width: 100%; border-collapse: collapse; font-size: 14px; }
        .table thead { background: var(--bg-secondary); }
        .table th {
            padding: 12px 24px; text-align: left;
            font-size: 12px; font-weight: 600; color: var(--text-secondary);
            text-transform: uppercase; letter-spacing: 0.5px;
        }
        .table td { padding: 16px 24px; border-top: 1px solid var(--border); color: var(--text-primary); vertical-align: middle; }
        .table tbody tr { transition: var(--transition); }
        .table tbody tr:hover { background: var(--bg-secondary); }
        .table code {
            font-family: 'Monaco', 'Menlo', 'Courier New', monospace;
            font-size: 12px; color: var(--primary);
            background: var(--info-light); padding: 2px 6px; border-radius: 4px;
        }
        .badge {
            display: inline-flex; align-items: center;
            padding: 4px 10px; border-radius: 9999px;
            font-size: 12px; font-weight: 500; line-height: 1;
        }
        .badge-success { background: var(--success-light); color: var(--success); }
        .badge-warning { background: var(--warning-light); color: var(--warning); }
        .badge-error   { background: var(--error-light);   color: var(--error); }
        .badge-info    { background: var(--info-light);    color: var(--info); }
        .badge-gray    { background: var(--bg-tertiary);   color: var(--text-secondary); }
        .progress-bar { height: 8px; background: var(--bg-tertiary); border-radius: 4px; overflow: hidden; }
        .progress-fill { height: 100%; background: linear-gradient(90deg, var(--primary), var(--secondary)); border-radius: 4px; }
        @keyframes fadeIn { from { opacity: 0; transform: translateY(8px); } to { opacity: 1; transform: translateY(0); } }
        .stat-card, .card { animation: fadeIn 0.3s ease-out; }
        .stat-card:nth-child(1) { animation-delay: 0.04s; }
        .stat-card:nth-child(2) { animation-delay: 0.08s; }
        .stat-card:nth-child(3) { animation-delay: 0.12s; }
        .stat-card:nth-child(4) { animation-delay: 0.16s; }
        .stat-card:nth-child(5) { animation-delay: 0.20s; }
        .stat-card:nth-child(6) { animation-delay: 0.24s; }
        ::-webkit-scrollbar { width: 6px; height: 6px; }
        ::-webkit-scrollbar-track { background: var(--bg-secondary); }
        ::-webkit-scrollbar-thumb { background: var(--border); border-radius: 3px; }
        ::-webkit-scrollbar-thumb:hover { background: var(--text-tertiary); }
        :focus-visible { outline: 2px solid var(--primary); outline-offset: 2px; }
        """;

    /// <summary>
    /// Wraps the content in the standard HTML shell with sidebar and navigation.
    /// </summary>
    /// <param name="title">The page title.</param>
    /// <param name="pathPrefix">The dashboard path prefix.</param>
    /// <param name="activeRoute">The currently active route for highlighting.</param>
    /// <param name="body">The main content HTML.</param>
    /// <param name="counters">Optional sidebar counters.</param>
    /// <param name="metrics">Optional metrics for health badge.</param>
    /// <returns>The complete HTML string.</returns>
    internal static string Wrap(
        string title, string pathPrefix, string activeRoute, string body,
        NavCounters? counters = null, JobMetrics? metrics = null) =>
        $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <title>{{title}} — NexJob</title>
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
                    <a href="{{pathPrefix}}" class="nav-item {{Active(activeRoute, "overview")}}">
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/></svg>
                        <span class="nav-label">Overview</span></a>
                    <a href="{{pathPrefix}}/queues" class="nav-item {{Active(activeRoute, "queues")}}">
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/></svg>
                        <span class="nav-label">Queues</span>{{NavCounter(counters?.Queues, counters?.QueuesClass)}}</a>
                    <a href="{{pathPrefix}}/servers" class="nav-item {{Active(activeRoute, "servers")}}">
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="2" width="20" height="8" rx="2" ry="2"/><rect x="2" y="14" width="20" height="8" rx="2" ry="2"/><line x1="6" y1="6" x2="6" y2="6"/><line x1="6" y1="18" x2="6" y2="18"/></svg>
                        <span class="nav-label">Servers</span>{{NavCounter(counters?.Servers, counters?.ServersClass)}}</a>
                    <a href="{{pathPrefix}}/jobs" class="nav-item {{Active(activeRoute, "jobs")}}">
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/><polyline points="10 9 9 9 8 9"/></svg>
                        <span class="nav-label">Jobs</span>{{NavCounter(counters?.Jobs, null)}}</a>
                    <a href="{{pathPrefix}}/recurring" class="nav-item {{Active(activeRoute, "recurring")}}">
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="23 4 23 10 17 10"/><polyline points="1 20 1 14 7 14"/><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"/></svg>
                        <span class="nav-label">Recurring</span>{{NavCounter(counters?.Recurring, null)}}</a>
                    <a href="{{pathPrefix}}/failed" class="nav-item {{Active(activeRoute, "failed")}}">
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/></svg>
                        <span class="nav-label">Failed</span>{{NavCounter(counters?.Failed, counters?.FailedClass)}}</a>
                    <a href="{{pathPrefix}}/settings" class="nav-item {{Active(activeRoute, "settings")}}">
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>
                        <span class="nav-label">Settings</span></a>
                </div>
                <div class="sidebar-footer">
                    <div class="version-info">v2.0.0</div>
                    <button id="nexjob-theme-toggle" class="theme-toggle" title="Toggle dark mode">🌙</button>
                </div>
            </nav>
            <main class="main-content">
                {{body}}
            </main>
        </div>
        <script>
        (function(){
            var h=document.documentElement,t=document.getElementById('nexjob-theme-toggle');
            var s=localStorage.getItem('nexjob-theme')||'light';
            h.setAttribute('data-theme',s);
            if(t) {
                t.textContent = s === 'dark' ? '☀️' : '🌙';
                t.addEventListener('click',function(){
                    var d=h.getAttribute('data-theme')==='dark';
                    var n=d?'light':'dark';
                    h.setAttribute('data-theme',n);
                    localStorage.setItem('nexjob-theme',n);
                    t.textContent = n === 'dark' ? '☀️' : '🌙';
                });
            }

            async function nexJobPoll() {
                setTimeout(nexJobPoll, 5000);
                var isInput = document.activeElement && ['INPUT','SELECT','TEXTAREA'].includes(document.activeElement.tagName);
                var isModal = document.querySelector('dialog[open]') !== null;
                if (isInput || isModal) return;
                var targets = document.querySelectorAll('[data-refresh="true"]');
                if (targets.length === 0) return;
                try {
                    var res = await fetch(window.location.href, { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
                    if (!res.ok) return;
                    var text = await res.text();
                    var doc = new DOMParser().parseFromString(text, 'text/html');
                    targets.forEach(function(el) {
                        if (el.id) {
                            var newEl = doc.getElementById(el.id);
                            if (newEl && el.innerHTML !== newEl.innerHTML) {
                                el.innerHTML = newEl.innerHTML;
                            }
                        }
                    });
                } catch(e) {}
            }
            setTimeout(nexJobPoll, 5000);
        })();
        </script>
        </body>
        </html>
        """;

    /// <summary>
    /// Generates a standard 404 Not Found page.
    /// </summary>
    /// <param name="title">The page title.</param>
    /// <param name="pathPrefix">The dashboard path prefix.</param>
    /// <returns>The complete HTML string.</returns>
    internal static string NotFound(string title, string pathPrefix) =>
        Wrap(title, pathPrefix, string.Empty,
            "<div class=\"empty-state\"><svg width=\"48\" height=\"48\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1\"><circle cx=\"12\" cy=\"12\" r=\"10\"/><line x1=\"12\" y1=\"8\" x2=\"12\" y2=\"12\"/><line x1=\"12\" y1=\"16\" x2=\"12.01\" y2=\"16\"/></svg><p>404 — Page not found</p></div>");

    private static string Active(string route, string page) =>
        string.Equals(route, page, StringComparison.Ordinal) ? "active" : string.Empty;

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
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var clsPart = string.IsNullOrEmpty(cls) ? string.Empty : " " + cls;
        return $"<span class=\"nav-counter{clsPart}\">{value}</span>";
    }
}
