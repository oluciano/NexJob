namespace NexJob.Dashboard;

/// <summary>Shared HTML shell (layout wrapper) injected around Blazor component output.</summary>
internal static class HtmlShell
{
    private const string Css =
        """
        :root {
            --bg: #0f0f0f;
            --surface: #1a1a1a;
            --surface2: #242424;
            --border: #2e2e2e;
            --accent: #7c3aed;
            --accent-light: #a78bfa;
            --text: #e5e5e5;
            --text-muted: #888;
            --success: #22c55e;
            --warning: #f59e0b;
            --danger: #ef4444;
            --info: #3b82f6;
        }
        *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
        body { font-family: 'Inter', system-ui, sans-serif; background: var(--bg); color: var(--text); font-size: 14px; }
        a { color: var(--accent-light); text-decoration: none; }
        a:hover { text-decoration: underline; }

        /* Layout */
        .layout { display: flex; min-height: 100vh; }
        .sidebar { width: 220px; background: var(--surface); border-right: 1px solid var(--border); flex-shrink: 0; display: flex; flex-direction: column; }
        .sidebar-header { padding: 20px 16px 16px; border-bottom: 1px solid var(--border); }
        .logo { font-size: 16px; font-weight: 700; color: var(--accent-light); }
        .nav-list { list-style: none; padding: 12px 0; }
        .nav-list li { margin: 2px 8px; }
        .nav-link { display: block; padding: 8px 12px; border-radius: 6px; color: var(--text-muted); transition: background .15s, color .15s; }
        .nav-link:hover { background: var(--surface2); color: var(--text); text-decoration: none; }
        .nav-link.active { background: var(--accent); color: #fff; }
        .content { flex: 1; padding: 28px; overflow-y: auto; }

        /* Cards */
        .cards { display: grid; grid-template-columns: repeat(auto-fill, minmax(160px, 1fr)); gap: 16px; margin-bottom: 28px; }
        .card { background: var(--surface); border: 1px solid var(--border); border-radius: 10px; padding: 20px 16px; }
        .card-label { color: var(--text-muted); font-size: 12px; text-transform: uppercase; letter-spacing: .05em; margin-bottom: 8px; }
        .card-value { font-size: 28px; font-weight: 700; }
        .card-value.enqueued  { color: var(--info); }
        .card-value.processing{ color: var(--warning); }
        .card-value.succeeded { color: var(--success); }
        .card-value.failed    { color: var(--danger); }
        .card-value.scheduled { color: var(--accent-light); }
        .card-value.recurring { color: var(--text); }

        /* Tables */
        .section { margin-bottom: 32px; }
        .section h2 { font-size: 15px; font-weight: 600; color: var(--text-muted); text-transform: uppercase; letter-spacing: .06em; margin-bottom: 14px; }
        table { width: 100%; border-collapse: collapse; background: var(--surface); border-radius: 10px; overflow: hidden; border: 1px solid var(--border); }
        th { background: var(--surface2); padding: 10px 14px; text-align: left; font-size: 11px; text-transform: uppercase; letter-spacing: .06em; color: var(--text-muted); border-bottom: 1px solid var(--border); }
        td { padding: 10px 14px; border-bottom: 1px solid var(--border); vertical-align: top; }
        tr:last-child td { border-bottom: none; }
        tr:hover td { background: var(--surface2); }

        /* Badges */
        .badge { display: inline-block; padding: 2px 8px; border-radius: 999px; font-size: 11px; font-weight: 600; }
        .badge-enqueued   { background: #1d3557; color: var(--info); }
        .badge-processing { background: #3d2a0a; color: var(--warning); }
        .badge-succeeded  { background: #0a2e1a; color: var(--success); }
        .badge-failed     { background: #2e0a0a; color: var(--danger); }
        .badge-scheduled  { background: #1e1040; color: var(--accent-light); }
        .badge-awaiting   { background: #1a1a2e; color: #a0aec0; }
        .badge-deleted    { background: #1a1a1a; color: #666; }

        /* Buttons */
        .btn { display: inline-block; padding: 6px 14px; border-radius: 6px; font-size: 13px; font-weight: 500; cursor: pointer; border: none; text-align: center; }
        .btn-primary { background: var(--accent); color: #fff; }
        .btn-danger  { background: #7f1d1d; color: #fca5a5; }
        .btn-sm { padding: 4px 10px; font-size: 12px; }
        .btn:hover { opacity: .85; }

        /* Detail */
        .detail-grid { display: grid; grid-template-columns: 200px 1fr; gap: 8px 16px; margin-bottom: 24px; }
        .detail-label { color: var(--text-muted); font-size: 12px; padding-top: 2px; }
        .detail-value { word-break: break-all; }
        pre { background: var(--surface2); border: 1px solid var(--border); border-radius: 8px; padding: 16px; overflow-x: auto; font-size: 12px; line-height: 1.6; white-space: pre-wrap; }

        /* Chart */
        .chart { background: var(--surface); border: 1px solid var(--border); border-radius: 10px; padding: 20px; margin-bottom: 28px; }
        .chart h2 { font-size: 13px; font-weight: 600; color: var(--text-muted); text-transform: uppercase; letter-spacing: .06em; margin-bottom: 16px; }
        .bars { display: flex; align-items: flex-end; gap: 4px; height: 80px; }
        .bar-wrap { flex: 1; display: flex; flex-direction: column; align-items: center; gap: 4px; }
        .bar { width: 100%; background: var(--accent); border-radius: 3px 3px 0 0; min-height: 2px; transition: height .3s; }
        .bar-label { font-size: 9px; color: var(--text-muted); }

        /* Filters */
        .filters { display: flex; gap: 10px; align-items: center; margin-bottom: 16px; flex-wrap: wrap; }
        .filters input, .filters select { background: var(--surface); border: 1px solid var(--border); border-radius: 6px; color: var(--text); padding: 6px 10px; font-size: 13px; }
        .filters input:focus, .filters select:focus { outline: 2px solid var(--accent); }

        /* Pagination */
        .pagination { display: flex; gap: 6px; margin-top: 16px; align-items: center; }
        .page-info { color: var(--text-muted); font-size: 12px; margin-left: 8px; }

        /* Page title */
        .page-title { font-size: 22px; font-weight: 700; margin-bottom: 24px; }

        @media (max-width: 700px) {
            .layout { flex-direction: column; }
            .sidebar { width: 100%; border-right: none; border-bottom: 1px solid var(--border); }
            .nav-list { display: flex; flex-wrap: wrap; padding: 8px; }
            .nav-list li { margin: 2px; }
        }
        """;

    internal static string Wrap(string title, string pathPrefix, string activeRoute, string body) =>
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
        <div class="layout">
            <nav class="sidebar">
                <div class="sidebar-header">
                    <span class="logo">⚡ {{title}}</span>
                </div>
                <ul class="nav-list">
                    <li><a href="{{pathPrefix}}" class="{{Active(activeRoute, "overview")}}">Overview</a></li>
                    <li><a href="{{pathPrefix}}/queues" class="{{Active(activeRoute, "queues")}}">Queues</a></li>
                    <li><a href="{{pathPrefix}}/jobs" class="{{Active(activeRoute, "jobs")}}">Jobs</a></li>
                    <li><a href="{{pathPrefix}}/recurring" class="{{Active(activeRoute, "recurring")}}">Recurring</a></li>
                    <li><a href="{{pathPrefix}}/failed" class="{{Active(activeRoute, "failed")}}">Failed</a></li>
                </ul>
            </nav>
            <main class="content">
                {{body}}
            </main>
        </div>
        </body>
        </html>
        """;

    internal static string NotFound(string title, string pathPrefix) =>
        Wrap(title, pathPrefix, string.Empty, "<h2>404 — Page not found</h2>");

    private static string Active(string route, string page) =>
        route == page ? "nav-link active" : "nav-link";
}
