namespace NexJob.Dashboard;

/// <summary>Shared HTML shell (layout wrapper) injected around page component output.</summary>
internal static class HtmlShell
{
    private const string Css =
        """
        :root {
            --bg:          #080810;
            --surface:     #0f0f1a;
            --surface2:    #16162a;
            --surface3:    #1e1e35;
            --accent:      #6366f1;
            --accent-glow: rgba(99,102,241,.15);
            --accent-light:#a5b4fc;
            --border:      rgba(255,255,255,.07);
            --border-hover:rgba(255,255,255,.14);
            --text:        #f1f5f9;
            --text-2:      #94a3b8;
            --text-3:      #475569;
            --success:     #34d399;
            --warning:     #fbbf24;
            --danger:      #f87171;
            --info:        #60a5fa;
            --success-bg:  rgba(52,211,153,.08);
            --warning-bg:  rgba(251,191,36,.08);
            --danger-bg:   rgba(248,113,113,.08);
            --info-bg:     rgba(96,165,250,.08);
        }
        *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
        body {
            font-family: 'Inter', -apple-system, BlinkMacSystemFont, sans-serif;
            background: var(--bg); color: var(--text);
            font-size: 13px; line-height: 1.6;
            font-feature-settings: 'cv02','cv03','cv04','cv11';
            -webkit-font-smoothing: antialiased;
        }
        a { color: var(--accent-light); text-decoration: none; }
        a:hover { text-decoration: underline; color: var(--text); }

        ::-webkit-scrollbar { width: 6px; height: 6px; }
        ::-webkit-scrollbar-track { background: transparent; }
        ::-webkit-scrollbar-thumb { background: var(--border-hover); border-radius: 3px; }
        ::-webkit-scrollbar-thumb:hover { background: var(--text-3); }
        :focus-visible { outline: 2px solid var(--accent); outline-offset: 2px; }

        /* Layout */
        .layout { display: flex; min-height: 100vh; }
        .sidebar {
            width: 240px; background: var(--surface);
            border-right: 1px solid var(--border); flex-shrink: 0;
            display: flex; flex-direction: column;
            position: sticky; top: 0; height: 100vh; overflow-y: auto;
        }
        .sidebar-header {
            padding: 20px 16px 18px; border-bottom: 1px solid var(--border);
            display: flex; align-items: center; gap: 10px;
        }
        .logo-text { font-size: 15px; font-weight: 600; color: var(--text); }
        .nav-section { padding: 10px 0; flex: 1; }
        .nav-list { list-style: none; padding: 2px 8px; }
        .nav-list li { margin: 1px 0; }
        .nav-link {
            display: flex; align-items: center; gap: 9px;
            padding: 7px 10px; border-radius: 7px;
            color: var(--text-2); font-size: 13px;
            transition: background .12s, color .12s;
            border-left: 2px solid transparent;
        }
        .nav-link:hover { background: var(--surface2); color: var(--text); text-decoration: none; }
        .nav-link.active {
            background: var(--accent-glow); color: var(--accent-light);
            border-left-color: var(--accent);
        }
        .nav-link svg { flex-shrink: 0; opacity: .7; }
        .nav-link.active svg { opacity: 1; }
        .sidebar-footer {
            padding: 12px 16px; border-top: 1px solid var(--border);
            font-size: 11px; color: var(--text-3);
            display: flex; justify-content: space-between; align-items: center;
        }
        .content { flex: 1; padding: 32px 36px; max-width: 1280px; overflow-y: auto; }

        /* Page header */
        .page-header {
            display: flex; align-items: flex-start;
            justify-content: space-between; margin-bottom: 28px;
            gap: 16px; flex-wrap: wrap;
        }
        .page-header-actions { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
        .page-title { font-size: 20px; font-weight: 600; letter-spacing: -.3px; color: var(--text); }
        .page-subtitle { font-size: 12px; color: var(--text-3); margin-top: 2px; }
        .section-title {
            font-size: 11px; font-weight: 600; letter-spacing: .08em;
            text-transform: uppercase; color: var(--text-3); margin-bottom: 12px;
        }

        /* Metric cards */
        .cards { display: grid; grid-template-columns: repeat(3, 1fr); gap: 14px; margin-bottom: 28px; }
        .card {
            background: var(--surface); border: 1px solid var(--border); border-radius: 12px;
            padding: 18px 16px;
            box-shadow: 0 1px 3px rgba(0,0,0,.4), inset 0 1px 0 rgba(255,255,255,.04);
            transition: all .15s ease;
        }
        .card:hover {
            box-shadow: 0 1px 3px rgba(0,0,0,.4), 0 0 0 1px var(--border-hover),
                        inset 0 1px 0 rgba(255,255,255,.06);
        }
        .card-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 14px; }
        .card-label {
            display: flex; align-items: center; gap: 6px;
            font-size: 11px; font-weight: 600; text-transform: uppercase;
            letter-spacing: .07em; color: var(--text-2);
        }
        .card-value { font-size: 32px; font-weight: 700; letter-spacing: -1px; line-height: 1; }
        .card-delta { font-size: 11px; color: var(--text-3); margin-top: 6px; }
        .card-enqueued   { border-top: 2px solid var(--info); }
        .card-processing { border-top: 2px solid var(--warning); }
        .card-succeeded  { border-top: 2px solid var(--success); }
        .card-failed     { border-top: 2px solid var(--danger); }
        .card-scheduled  { border-top: 2px solid var(--accent-light); }
        .card-recurring  { border-top: 2px solid var(--text-3); }
        .card-enqueued   .card-value { color: var(--info); }
        .card-processing .card-value { color: var(--warning); }
        .card-succeeded  .card-value { color: var(--success); }
        .card-failed     .card-value { color: var(--danger); }
        .card-scheduled  .card-value { color: var(--accent-light); }
        .card-recurring  .card-value { color: var(--text-2); }

        /* Status dots */
        .dot {
            display: inline-block; width: 6px; height: 6px; border-radius: 50%;
            vertical-align: middle; margin-right: 6px; flex-shrink: 0;
        }
        .dot-processing { background: var(--warning); animation: pulse 2s ease-in-out infinite; }
        .dot-succeeded  { background: var(--success); }
        .dot-failed     { background: var(--danger); }
        .dot-scheduled  { background: var(--accent-light); }
        .dot-enqueued   { background: var(--info); }
        .dot-awaiting   { background: var(--text-3); }
        .dot-default    { background: var(--text-3); }
        @keyframes pulse { 0%,100%{opacity:1} 50%{opacity:.35} }

        /* Badges */
        .badge {
            display: inline-flex; align-items: center; gap: 4px;
            padding: 2px 7px; border-radius: 5px;
            font-size: 11px; font-weight: 600; line-height: 1.4;
        }
        .badge-enqueued   { background: var(--info-bg);    color: var(--info); }
        .badge-processing { background: var(--warning-bg); color: var(--warning); }
        .badge-succeeded  { background: var(--success-bg); color: var(--success); }
        .badge-failed     { background: var(--danger-bg);  color: var(--danger); }
        .badge-scheduled  { background: var(--accent-glow);color: var(--accent-light); }
        .badge-awaiting   { background: rgba(148,163,184,.08); color: var(--text-2); }
        .badge-deleted    { background: rgba(71,85,105,.12); color: var(--text-3); }

        /* Tables */
        .section { margin-bottom: 28px; }
        table { width: 100%; border-collapse: collapse; background: var(--surface); border-radius: 10px; overflow: hidden; border: 1px solid var(--border); }
        th { background: var(--surface2); padding: 9px 14px; text-align: left; font-size: 10px; text-transform: uppercase; letter-spacing: .07em; color: var(--text-3); border-bottom: 1px solid var(--border); font-weight: 600; }
        td { padding: 10px 14px; border-bottom: 1px solid var(--border); vertical-align: middle; }
        tr:last-child td { border-bottom: none; }
        tr:hover td { background: var(--surface2); }

        /* Job rows */
        .job-list { display: flex; flex-direction: column; gap: 6px; }
        .job-row {
            display: grid; grid-template-columns: 10px 1fr auto;
            gap: 0 14px; padding: 13px 16px;
            background: var(--surface); border: 1px solid var(--border);
            border-radius: 10px; cursor: pointer;
            transition: border-color .12s, background .12s; align-items: start;
        }
        .job-row:hover { border-color: var(--border-hover); background: var(--surface2); }
        .job-row-dot { margin-top: 4px; }
        .job-row-main { min-width: 0; }
        .job-row-meta { text-align: right; font-size: 12px; color: var(--text-3); white-space: nowrap; }
        .job-row-title { font-size: 13px; font-weight: 500; color: var(--text); margin-bottom: 2px; }
        .job-row-sub { font-size: 12px; color: var(--text-3); display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
        .job-row-tags { display: flex; gap: 4px; flex-wrap: wrap; margin-top: 6px; }

        /* Buttons */
        .btn {
            display: inline-flex; align-items: center; gap: 5px;
            padding: 6px 14px; border-radius: 7px;
            font-size: 12px; font-weight: 500; cursor: pointer;
            border: 1px solid transparent; text-align: center;
            transition: all .12s ease; line-height: 1;
        }
        .btn-primary { background: var(--accent); color: #fff; border-color: var(--accent); }
        .btn-primary:hover { background: #4f51d4; border-color: #4f51d4; text-decoration: none; color: #fff; }
        .btn-danger  { background: var(--danger-bg); color: var(--danger); border-color: rgba(248,113,113,.2); }
        .btn-danger:hover { background: rgba(248,113,113,.18); }
        .btn-ghost   { background: var(--surface2); color: var(--text-2); border-color: var(--border); }
        .btn-ghost:hover { border-color: var(--border-hover); color: var(--text); }
        .btn-sm { padding: 4px 10px; font-size: 11px; }

        /* Filters */
        .filters { display: flex; gap: 8px; align-items: center; margin-bottom: 18px; flex-wrap: wrap; }
        .filters input, .filters select {
            background: var(--surface); border: 1px solid var(--border);
            border-radius: 7px; color: var(--text); padding: 6px 10px;
            font-size: 12px; transition: border-color .12s;
        }
        .filters input:focus, .filters select:focus { outline: none; border-color: var(--accent); }
        .filters input::placeholder { color: var(--text-3); }
        .status-pills { display: flex; gap: 4px; flex-wrap: wrap; }
        .status-pill {
            padding: 4px 11px; border-radius: 20px; font-size: 11px; font-weight: 500;
            cursor: pointer; border: 1px solid var(--border); background: var(--surface);
            color: var(--text-2); transition: all .12s ease; text-decoration: none;
        }
        .status-pill:hover { border-color: var(--border-hover); color: var(--text); text-decoration: none; }
        .status-pill.active { background: var(--accent-glow); color: var(--accent-light); border-color: rgba(99,102,241,.3); }

        /* Chart */
        .chart {
            background: var(--surface); border: 1px solid var(--border);
            border-radius: 12px; padding: 20px; margin-bottom: 28px; position: relative;
        }
        .chart-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 16px; }
        .bars { display: flex; align-items: flex-end; gap: 3px; height: 160px; position: relative; padding-bottom: 20px; }
        .bar-wrap { flex: 1; display: flex; flex-direction: column; align-items: center; position: relative; height: 100%; justify-content: flex-end; }
        .bar {
            width: 100%; border-radius: 3px 3px 0 0; min-height: 2px;
            background: linear-gradient(to bottom, var(--accent), rgba(99,102,241,.25));
            cursor: pointer; transition: opacity .15s; position: relative;
        }
        .bar:hover { opacity: .75; }
        .bar-label { font-size: 9px; color: var(--text-3); position: absolute; bottom: 0; white-space: nowrap; }
        .chart-tooltip {
            display: none; position: fixed;
            background: var(--surface3); border: 1px solid var(--border-hover);
            border-radius: 6px; padding: 6px 10px; font-size: 12px; color: var(--text);
            pointer-events: none; z-index: 100; white-space: nowrap;
            box-shadow: 0 4px 12px rgba(0,0,0,.4);
        }

        /* Detail grid (grouped sections) */
        .detail-sections { display: flex; flex-direction: column; gap: 16px; margin-bottom: 24px; }
        .detail-section { background: var(--surface); border: 1px solid var(--border); border-radius: 10px; overflow: hidden; }
        .detail-section-header {
            padding: 8px 14px; background: var(--surface2); border-bottom: 1px solid var(--border);
            font-size: 10px; font-weight: 600; letter-spacing: .08em; text-transform: uppercase; color: var(--text-3);
        }
        .detail-grid { display: grid; grid-template-columns: 180px 1fr; }
        .detail-label { padding: 9px 14px; color: var(--text-3); font-size: 12px; border-bottom: 1px solid var(--border); }
        .detail-value { padding: 9px 14px; word-break: break-all; font-size: 13px; border-bottom: 1px solid var(--border); }
        .detail-label:last-of-type, .detail-value:last-of-type { border-bottom: none; }

        /* Code & pre */
        pre {
            background: var(--surface2); border: 1px solid var(--border); border-radius: 8px;
            padding: 16px; overflow-x: auto; font-size: 12px; line-height: 1.7;
            white-space: pre-wrap; word-break: break-word;
            font-family: 'JetBrains Mono', 'Fira Code', ui-monospace, monospace;
        }
        .jk { color: #94a3b8; }
        .js { color: #a5b4fc; }
        .jn { color: #34d399; }
        .jb { color: #fbbf24; }

        /* Log terminal */
        .log-terminal {
            background: #020207; border: 1px solid rgba(255,255,255,.06); border-radius: 8px;
            padding: 14px 16px; overflow-x: auto; font-size: 12px; line-height: 1.8;
            word-break: break-word;
            font-family: 'JetBrains Mono', 'Fira Code', ui-monospace, monospace;
        }

        /* Progress bar */
        .progress-wrap { margin: 12px 0; }
        .progress-bar-track { background: var(--surface2); border-radius: 999px; height: 8px; overflow: hidden; }
        .progress-bar-fill {
            height: 100%; background: linear-gradient(to right, var(--accent), var(--accent-light));
            border-radius: 999px; transition: width .5s ease;
        }
        .progress-info { display: flex; align-items: center; gap: 8px; margin-top: 6px; font-size: 12px; color: var(--text-2); }
        .progress-pct { font-weight: 600; color: var(--accent-light); }

        /* Tag badges */
        .tag-badge {
            display: inline-flex; align-items: center; padding: 2px 8px; border-radius: 5px;
            font-size: 11px; font-weight: 500;
            background: var(--accent-glow); color: var(--accent-light);
            border: 1px solid rgba(99,102,241,.2); white-space: nowrap; transition: all .12s ease;
        }
        .tag-badge:hover { background: rgba(99,102,241,.25); text-decoration: none; color: var(--accent-light); }

        /* Queue cards */
        .queue-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 14px; }
        .queue-card {
            background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 18px;
            box-shadow: 0 1px 3px rgba(0,0,0,.4), inset 0 1px 0 rgba(255,255,255,.04);
            transition: all .15s ease;
        }
        .queue-card:hover { border-color: var(--border-hover); }
        .queue-card-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 14px; }
        .queue-name { font-size: 14px; font-weight: 600; color: var(--text); }
        .queue-metrics { display: flex; gap: 24px; margin-bottom: 14px; }
        .queue-metric-label { font-size: 10px; text-transform: uppercase; letter-spacing: .07em; color: var(--text-3); font-weight: 600; margin-bottom: 2px; }
        .queue-metric-val { font-size: 20px; font-weight: 700; letter-spacing: -.5px; }
        .queue-util-bar { background: var(--surface2); border-radius: 999px; height: 6px; overflow: hidden; }
        .queue-util-fill { height: 100%; background: var(--accent); border-radius: 999px; }
        .queue-util-label { font-size: 11px; color: var(--text-3); margin-top: 4px; }

        /* Recurring cards */
        .recurring-list { display: flex; flex-direction: column; gap: 8px; }
        .recurring-card {
            background: var(--surface); border: 1px solid var(--border);
            border-radius: 10px; padding: 14px 16px; transition: border-color .12s;
        }
        .recurring-card:hover { border-color: var(--border-hover); }
        .recurring-card-header { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
        .recurring-card-left { display: flex; align-items: center; gap: 8px; min-width: 0; }
        .recurring-card-right { display: flex; align-items: center; gap: 6px; flex-shrink: 0; }
        .recurring-card-meta { font-size: 12px; color: var(--text-3); margin-top: 6px; display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
        .recurring-id { font-size: 13px; font-weight: 600; color: var(--text); }
        code.cron { font-family: 'JetBrains Mono', ui-monospace, monospace; font-size: 11px; background: var(--surface2); padding: 2px 7px; border-radius: 4px; color: var(--warning); border: 1px solid var(--border); }

        /* Settings */
        .settings-card { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; overflow: hidden; margin-bottom: 16px; }
        .settings-card-header { padding: 12px 16px; background: var(--surface2); border-bottom: 1px solid var(--border); font-size: 11px; font-weight: 600; letter-spacing: .08em; text-transform: uppercase; color: var(--text-2); }
        .settings-card-body { padding: 4px 0; }
        .settings-row { display: flex; align-items: center; gap: 12px; padding: 11px 16px; border-bottom: 1px solid var(--border); }
        .settings-row:last-child { border-bottom: none; }
        .settings-row-label { flex: 1; font-size: 13px; color: var(--text); }
        .settings-row-sub { font-size: 11px; color: var(--text-3); margin-top: 1px; }

        /* Toggle switch */
        .toggle { position: relative; width: 36px; height: 20px; display: inline-block; flex-shrink: 0; }
        .toggle input { display: none; }
        .toggle-thumb { position: absolute; inset: 0; background: var(--surface3); border-radius: 20px; cursor: pointer; transition: .2s; border: 1px solid var(--border); }
        .toggle-thumb::before { content: ''; position: absolute; width: 14px; height: 14px; left: 2px; top: 2px; background: var(--text-3); border-radius: 50%; transition: .2s; }
        .toggle input:checked + .toggle-thumb { background: var(--accent); border-color: var(--accent); }
        .toggle input:checked + .toggle-thumb::before { transform: translateX(16px); background: #fff; }

        /* Empty state */
        .empty-state { display: flex; flex-direction: column; align-items: center; padding: 60px 20px; color: var(--text-3); text-align: center; }
        .empty-state svg { margin-bottom: 16px; opacity: .35; }
        .empty-state p { font-size: 14px; }

        /* Pagination */
        .pagination { display: flex; gap: 6px; margin-top: 16px; align-items: center; }
        .page-info { color: var(--text-3); font-size: 12px; margin-left: 6px; }

        /* Alert banner */
        .alert { border-radius: 8px; padding: 10px 14px; font-size: 12px; margin-bottom: 16px; display: flex; align-items: center; gap: 8px; }
        .alert-warning { background: var(--warning-bg); border: 1px solid rgba(251,191,36,.2); color: var(--warning); }
        .alert-danger  { background: var(--danger-bg);  border: 1px solid rgba(248,113,113,.2); color: var(--danger); }

        /* Responsive */
        @media (max-width: 1024px) {
            .cards { grid-template-columns: repeat(2, 1fr); }
            .content { padding: 20px 24px; }
            .queue-grid { grid-template-columns: 1fr; }
        }
        @media (max-width: 768px) {
            .layout { flex-direction: column; }
            .sidebar { width: 100%; height: auto; border-right: none; border-bottom: 1px solid var(--border); position: static; }
            .nav-section { padding: 4px 0; }
            .nav-list { display: flex; overflow-x: auto; padding: 4px 8px; gap: 2px; flex-wrap: nowrap; }
            .nav-list li { margin: 0; flex-shrink: 0; }
            .nav-link { padding: 6px 10px; border-left: none; border-bottom: 2px solid transparent; border-radius: 6px; }
            .nav-link.active { border-bottom-color: var(--accent); border-left-color: transparent; }
            .nav-label { display: none; }
            .cards { grid-template-columns: repeat(2, 1fr); }
            .content { padding: 16px; }
            .page-title { font-size: 17px; }
            .queue-grid { grid-template-columns: 1fr; }
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
                    <svg width="22" height="22" viewBox="0 0 20 20" aria-hidden="true">
                        <polygon points="10,1 18,5.5 18,14.5 10,19 2,14.5 2,5.5"
                                 fill="none" stroke="var(--accent)" stroke-width="1.5"/>
                        <circle cx="10" cy="10" r="2.5" fill="var(--accent)"/>
                        <line x1="10" y1="7.5" x2="10" y2="3" stroke="var(--accent)" stroke-width="1"/>
                        <line x1="12.2" y1="11.3" x2="15.6" y2="13.2" stroke="var(--accent)" stroke-width="1"/>
                        <line x1="7.8" y1="11.3" x2="4.4" y2="13.2" stroke="var(--accent)" stroke-width="1"/>
                    </svg>
                    <span class="logo-text">{{title}}</span>
                </div>
                <div class="nav-section">
                    <ul class="nav-list">
                        <li><a href="{{pathPrefix}}" class="nav-link {{Active(activeRoute, "overview")}}">
                            <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5"><rect x="1" y="1" width="6" height="6" rx="1"/><rect x="9" y="1" width="6" height="6" rx="1"/><rect x="1" y="9" width="6" height="6" rx="1"/><rect x="9" y="9" width="6" height="6" rx="1"/></svg>
                            <span class="nav-label">Overview</span></a></li>
                        <li><a href="{{pathPrefix}}/queues" class="nav-link {{Active(activeRoute, "queues")}}">
                            <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5"><rect x="1" y="10" width="14" height="4" rx="1"/><rect x="1" y="6" width="14" height="3" rx="1" opacity=".6"/><rect x="1" y="2" width="14" height="3" rx="1" opacity=".35"/></svg>
                            <span class="nav-label">Queues</span></a></li>
                        <li><a href="{{pathPrefix}}/jobs" class="nav-link {{Active(activeRoute, "jobs")}}">
                            <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5"><line x1="3" y1="4" x2="13" y2="4"/><line x1="3" y1="8" x2="13" y2="8"/><line x1="3" y1="12" x2="9" y2="12"/></svg>
                            <span class="nav-label">Jobs</span></a></li>
                        <li><a href="{{pathPrefix}}/recurring" class="nav-link {{Active(activeRoute, "recurring")}}">
                            <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M13.5 8A5.5 5.5 0 1 1 8 2.5"/><polyline points="11,1 14,2.5 11,4"/></svg>
                            <span class="nav-label">Recurring</span></a></li>
                        <li><a href="{{pathPrefix}}/failed" class="nav-link {{Active(activeRoute, "failed")}}">
                            <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5"><circle cx="8" cy="8" r="6.5"/><line x1="5.5" y1="5.5" x2="10.5" y2="10.5"/><line x1="10.5" y1="5.5" x2="5.5" y2="10.5"/></svg>
                            <span class="nav-label">Failed</span></a></li>
                        <li><a href="{{pathPrefix}}/settings" class="nav-link {{Active(activeRoute, "settings")}}">
                            <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5"><circle cx="8" cy="8" r="2.5"/><path d="M8 1.5v2M8 12.5v2M1.5 8h2M12.5 8h2M3.4 3.4l1.4 1.4M11.2 11.2l1.4 1.4M11.2 3.4l-1.4 1.4M4.6 11.2l-1.4 1.4"/></svg>
                            <span class="nav-label">Settings</span></a></li>
                    </ul>
                </div>
                <div class="sidebar-footer">
                    <span>v0.3.1</span>
                    <a href="https://github.com/oluciano/NexJob" style="color:var(--text-3);font-size:11px">GitHub ↗</a>
                </div>
            </nav>
            <main class="content">
                {{body}}
            </main>
        </div>
        </body>
        </html>
        """;

    internal static string NotFound(string title, string pathPrefix) =>
        Wrap(title, pathPrefix, string.Empty,
            "<div class=\"empty-state\"><svg width=\"48\" height=\"48\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1\"><circle cx=\"12\" cy=\"12\" r=\"10\"/><line x1=\"12\" y1=\"8\" x2=\"12\" y2=\"12\"/><line x1=\"12\" y1=\"16\" x2=\"12.01\" y2=\"16\"/></svg><p>404 — Page not found</p></div>");

    private static string Active(string route, string page) =>
        route == page ? "active" : string.Empty;
}
