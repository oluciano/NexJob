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

        /* ── Health badge ── */
        .health-badge {
            margin: 8px 16px 4px;
            padding: 6px 12px;
            border-radius: var(--radius);
            font-size: 11px; font-weight: 700; letter-spacing: .1em;
            display: flex; align-items: center; gap: 6px;
        }
        .health-badge.healthy  { background: var(--success-light); color: var(--success); }
        .health-badge.degraded { background: var(--warning-light); color: var(--warning); }
        .health-badge.incident { background: var(--error-light);   color: var(--error); }
        .health-pulse {
            width: 6px; height: 6px; border-radius: 50%;
            background: currentColor;
            animation: pulse 2s ease-in-out infinite;
        }
        @keyframes pulse { 0%,100% { opacity:1; } 50% { opacity:.3; } }

        /* ── Nav counter ── */
        .nav-counter {
            margin-left: auto; font-size: 11px; font-weight: 600;
            padding: 1px 6px; border-radius: 9999px;
            background: var(--bg-tertiary); color: var(--text-secondary);
        }
        .nav-counter.error { background: var(--error-light); color: var(--error); }
        .nav-counter.warn  { background: var(--warning-light); color: var(--warning); }

        /* ── Alert / banner ── */
        .alert {
            padding: 10px 14px; border-radius: var(--radius);
            font-size: 13px; display: flex; align-items: center; gap: 8px;
            border: 1px solid;
        }
        .alert-warning { background: var(--warning-light); color: var(--warning); border-color: var(--warning); }
        .alert-error   { background: var(--error-light);   color: var(--error);   border-color: var(--error); }

        /* ── Empty state ── */
        .empty-state {
            padding: 48px; text-align: center; color: var(--text-tertiary);
            display: flex; flex-direction: column; align-items: center; gap: 12px;
        }

        /* ── Status dot ── */
        .dot {
            display: inline-block; width: 7px; height: 7px;
            border-radius: 50%; flex-shrink: 0; vertical-align: middle; margin-right: 6px;
        }
        .dot-processing { background: var(--warning); animation: pulse 2s ease-in-out infinite; }
        .dot-succeeded  { background: var(--success); }
        .dot-failed     { background: var(--error); }
        .dot-scheduled  { background: var(--info); }
        .dot-enqueued   { background: var(--info); }
        .dot-awaiting   { background: var(--text-tertiary); }
        .dot-expired    { background: var(--text-tertiary); opacity: .5; }
        .dot-default    { background: var(--text-tertiary); }

        /* ── Job list / rows ── */
        .job-list { display: flex; flex-direction: column; }
        .job-row {
            display: grid;
            grid-template-columns: 32px 1fr auto;
            gap: 12px; align-items: start;
            padding: 14px 24px;
            border-bottom: 1px solid var(--border);
            transition: var(--transition);
        }
        .job-row:last-child { border-bottom: none; }
        .job-row:hover { background: var(--bg-secondary); }
        .job-row-dot   { padding-top: 4px; }
        .job-row-main  { min-width: 0; }
        .job-row-title { font-size: 14px; font-weight: 500; color: var(--text-primary); margin-bottom: 3px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
        .job-row-sub   { font-size: 12px; color: var(--text-secondary); display: flex; gap: 8px; flex-wrap: wrap; align-items: center; }
        .job-row-meta  { display: flex; flex-direction: column; align-items: flex-end; gap: 4px; font-size: 12px; color: var(--text-secondary); white-space: nowrap; }
        .job-row-tags  { display: flex; gap: 4px; flex-wrap: wrap; margin-top: 4px; }
        .job-name      { font-family: 'Monaco','Menlo','Courier New',monospace; font-size: 12px; color: var(--primary); }
        .job-type      { font-size: 11px; color: var(--text-tertiary); display: block; }
        .tag-badge {
            display: inline-block; padding: 1px 6px; border-radius: 4px;
            font-size: 11px; background: var(--bg-tertiary); color: var(--text-secondary);
            border: 1px solid var(--border);
        }

        /* ── Pagination ── */
        .pagination { display: flex; align-items: center; gap: 4px; justify-content: center; padding: 16px; }
        .page-info  { font-size: 12px; color: var(--text-tertiary); padding: 0 8px; }

        /* ── Filters ── */
        .filters {
            display: flex; gap: 8px; flex-wrap: wrap;
            margin-bottom: 16px; align-items: center;
        }
        .btn-ghost {
            background: transparent; border: 1px solid var(--border);
            color: var(--text-secondary); padding: 6px 12px;
            border-radius: var(--radius); font-size: 13px; cursor: pointer;
            transition: var(--transition);
        }
        .btn-ghost:hover, .btn-ghost.active { background: var(--bg-tertiary); color: var(--text-primary); }
        .btn-sm { padding: 5px 10px; font-size: 12px; }
        .btn-icon {
            width: 36px; height: 36px; border-radius: var(--radius);
            border: 1px solid var(--border); background: transparent;
            color: var(--text-secondary);
            display: inline-flex; align-items: center; justify-content: center;
            cursor: pointer; transition: var(--transition);
        }
        .btn-icon:hover { background: var(--bg-secondary); color: var(--text-primary); }

        /* ── Throughput chart ── */
        .chart { background: var(--bg-primary); border: 1px solid var(--border); border-radius: var(--radius-lg); padding: 20px 24px; margin-bottom: 24px; }
        .chart-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
        .chart-header h3 { font-size: 15px; font-weight: 600; color: var(--text-primary); }
        .bars { display: flex; align-items: flex-end; gap: 3px; height: 140px; padding-bottom: 20px; position: relative; }
        .bar-wrap { display: flex; flex-direction: column; align-items: center; position: relative; flex: 1; }
        .bar {
            width: 100%; min-height: 2px; border-radius: 3px 3px 0 0;
            background: linear-gradient(180deg, var(--primary), var(--primary-dark));
            transition: var(--transition); cursor: pointer; position: relative;
        }
        .bar:hover { opacity: .8; }
        .bar.anomaly { background: linear-gradient(180deg, var(--warning), var(--error)); }
        .bar-label { font-size: 10px; color: var(--text-tertiary); position: absolute; bottom: -18px; white-space: nowrap; }
        .chart-tooltip { display: none; position: absolute; bottom: calc(100% + 6px); left: 50%; transform: translateX(-50%); background: var(--bg-tertiary); border: 1px solid var(--border); border-radius: var(--radius-sm); padding: 4px 8px; font-size: 11px; white-space: nowrap; pointer-events: none; z-index: 10; }
        .bar-wrap:hover .chart-tooltip { display: block; }
        .anomaly-note { font-size: 12px; color: var(--warning); margin-top: 8px; display: flex; align-items: center; gap: 6px; }

        /* ── Queue grid / cards ── */
        .queue-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 16px; }
        .queue-card { background: var(--bg-primary); border: 1px solid var(--border); border-radius: var(--radius-lg); padding: 20px; transition: var(--transition); }
        .queue-card:hover { box-shadow: var(--shadow-md); }
        .queue-card-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 12px; }
        .queue-name { font-size: 15px; font-weight: 600; color: var(--text-primary); }
        .queue-metrics { display: grid; grid-template-columns: repeat(3, 1fr); gap: 8px; margin: 12px 0; }
        .queue-metric-val   { font-size: 20px; font-weight: 700; color: var(--text-primary); }
        .queue-metric-label { font-size: 11px; color: var(--text-tertiary); margin-top: 2px; }
        .queue-util-bar     { height: 4px; background: var(--bg-tertiary); border-radius: 2px; margin-top: 8px; overflow: hidden; }
        .queue-util-fill    { height: 100%; background: linear-gradient(90deg, var(--primary), var(--secondary)); border-radius: 2px; }
        .queue-util-label   { font-size: 11px; color: var(--text-tertiary); margin-top: 4px; }

        /* ── Worker heatmap ── */
        .worker-list { display: flex; flex-direction: column; gap: 6px; margin-bottom: 24px; }
        .worker-row  { display: flex; align-items: center; gap: 10px; padding: 10px 14px; background: var(--bg-secondary); border-radius: var(--radius); border: 1px solid var(--border); font-size: 13px; }
        .worker-row.idle    { opacity: .6; }
        .worker-row.worker-warn { border-color: var(--warning); background: var(--warning-light); }
        .worker-id       { font-size: 11px; color: var(--text-tertiary); width: 60px; flex-shrink: 0; }
        .worker-track    { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .worker-job-name { font-size: 13px; color: var(--text-primary); }
        .worker-elapsed  { font-size: 12px; color: var(--text-secondary); white-space: nowrap; flex-shrink: 0; }

        /* ── Detail page ── */
        .detail-sections { display: grid; gap: 24px; }
        .detail-section  { background: var(--bg-primary); border: 1px solid var(--border); border-radius: var(--radius-lg); overflow: hidden; }
        .detail-section-header { padding: 16px 20px; border-bottom: 1px solid var(--border); font-size: 13px; font-weight: 600; color: var(--text-secondary); text-transform: uppercase; letter-spacing: .06em; }
        .detail-grid  { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 0; }
        .detail-row   { padding: 14px 20px; border-bottom: 1px solid var(--border-light); }
        .detail-row:last-child { border-bottom: none; }
        .detail-label { font-size: 11px; color: var(--text-tertiary); text-transform: uppercase; letter-spacing: .06em; margin-bottom: 4px; }
        .detail-value { font-size: 14px; color: var(--text-primary); word-break: break-all; }

        /* ── Timeline (job detail) ── */
        .timeline { padding: 16px 20px; }
        .timeline-item { display: flex; gap: 12px; position: relative; padding-bottom: 16px; }
        .timeline-item:last-child { padding-bottom: 0; }
        .timeline-line  { width: 1px; background: var(--border); position: absolute; left: 7px; top: 16px; bottom: 0; }
        .timeline-content { flex: 1; min-width: 0; }
        .timeline-label { font-size: 13px; font-weight: 500; color: var(--text-primary); }
        .timeline-time  { font-size: 11px; color: var(--text-tertiary); margin-top: 2px; }
        .timeline-error { font-size: 12px; color: var(--error); margin-top: 6px; font-family: monospace; white-space: pre-wrap; word-break: break-word; }
        .timeline-section { font-size: 11px; font-weight: 600; color: var(--text-tertiary); text-transform: uppercase; letter-spacing: .06em; padding: 8px 20px 4px; }
        .timeline-metadata { font-size: 12px; color: var(--text-secondary); margin-top: 4px; }

        /* ── Log terminal ── */
        .log-terminal {
            background: var(--bg-tertiary); border-radius: var(--radius);
            padding: 16px; font-family: 'Monaco','Menlo','Courier New',monospace;
            font-size: 12px; line-height: 1.7; color: var(--text-secondary);
            max-height: 320px; overflow-y: auto; white-space: pre-wrap; word-break: break-all;
        }

        /* ── Recurring cards ── */
        .recurring-card { background: var(--bg-primary); border: 1px solid var(--border); border-radius: var(--radius-lg); padding: 20px; transition: var(--transition); }
        .recurring-card:hover { box-shadow: var(--shadow-md); }
        .recurring-card-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 12px; }
        .recurring-card-left   { flex: 1; min-width: 0; }
        .recurring-card-right  { display: flex; flex-direction: column; align-items: flex-end; gap: 6px; }
        .recurring-card-meta   { font-size: 12px; color: var(--text-secondary); margin-top: 6px; display: flex; flex-wrap: wrap; gap: 12px; }
        .recurring-id   { font-family: monospace; font-size: 13px; font-weight: 600; color: var(--text-primary); margin-bottom: 4px; }
        .cron           { font-family: monospace; font-size: 12px; color: var(--primary); background: var(--info-light); padding: 2px 6px; border-radius: 4px; }
        .cron-cell      { font-family: monospace; font-size: 12px; }
        .btn-trigger    { background: var(--info-light); color: var(--info); border: 1px solid var(--info); padding: 5px 10px; border-radius: var(--radius-sm); font-size: 12px; cursor: pointer; transition: var(--transition); }
        .btn-trigger:hover { background: var(--info); color: white; }
        .btn-pause      { background: var(--warning-light); color: var(--warning); border: 1px solid var(--warning); padding: 5px 10px; border-radius: var(--radius-sm); font-size: 12px; cursor: pointer; transition: var(--transition); }
        .btn-resume     { background: var(--success-light); color: var(--success); border: 1px solid var(--success); padding: 5px 10px; border-radius: var(--radius-sm); font-size: 12px; cursor: pointer; transition: var(--transition); }

        /* ── Settings ── */
        .settings-card        { background: var(--bg-primary); border: 1px solid var(--border); border-radius: var(--radius-lg); overflow: hidden; margin-bottom: 16px; }
        .settings-card-header { padding: 16px 20px; border-bottom: 1px solid var(--border); font-size: 15px; font-weight: 600; color: var(--text-primary); }
        .settings-card-body   { padding: 0; }
        .settings-row         { display: flex; justify-content: space-between; align-items: center; padding: 16px 20px; border-bottom: 1px solid var(--border-light); }
        .settings-row:last-child { border-bottom: none; }
        .settings-row-label   { font-size: 14px; font-weight: 500; color: var(--text-primary); }
        .settings-row-sub     { font-size: 12px; color: var(--text-secondary); margin-top: 2px; }
        .toggle       { width: 40px; height: 22px; border-radius: 11px; background: var(--border); position: relative; cursor: pointer; transition: var(--transition); border: none; }
        .toggle.on    { background: var(--success); }
        .toggle-thumb { width: 16px; height: 16px; border-radius: 50%; background: white; position: absolute; top: 3px; left: 3px; transition: var(--transition); box-shadow: var(--shadow-sm); }
        .toggle.on .toggle-thumb { transform: translateX(18px); }

        /* ── Section title ── */
        .section-title { font-size: 10px; font-weight: 700; letter-spacing: .1em; text-transform: uppercase; color: var(--text-tertiary); margin-bottom: 8px; }
        .section { margin-bottom: 24px; }

        /* ── Modal / dialog ── */
        dialog { background: var(--bg-primary); color: var(--text-primary); border: 1px solid var(--border); border-radius: var(--radius-lg); padding: 0; max-width: 560px; width: 90vw; }
        dialog::backdrop { background: rgba(0,0,0,.5); }

        /* ── Table extras ── */
        .table-recurring { table-layout: fixed; }
        .job-name-cell  { max-width: 220px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .status-cell    { width: 110px; }
        .actions-cell   { width: 80px; text-align: right; }

        /* ── Progress (job detail) ── */
        .progress-wrap       { margin: 8px 0; }
        .progress-bar-track  { height: 6px; background: var(--bg-tertiary); border-radius: 3px; overflow: hidden; }
        .progress-bar-fill   { height: 100%; background: linear-gradient(90deg, var(--primary), var(--secondary)); border-radius: 3px; transition: width .3s ease; }
        .progress-info       { display: flex; justify-content: space-between; font-size: 11px; color: var(--text-tertiary); margin-top: 4px; }
        .progress-pct        { font-weight: 600; color: var(--primary); }

        /* ── Legacy badge aliases (pages that still use old class names) ── */
        .badge-succeeded  { background: var(--success-light); color: var(--success); }
        .badge-processing { background: var(--warning-light); color: var(--warning); }
        .badge-failed     { background: var(--error-light);   color: var(--error); }
        .badge-enqueued   { background: var(--info-light);    color: var(--info); }
        .badge-awaiting   { background: var(--info-light);    color: var(--info); }
        .badge-scheduled  { background: var(--bg-tertiary);   color: var(--text-secondary); }
        .badge-expired    { background: var(--bg-tertiary);   color: var(--text-tertiary); }
        .badge-deleted    { background: var(--bg-tertiary);   color: var(--text-tertiary); }

        /* ── Page title (alias) ── */
        .page-title { font-size: 28px; font-weight: 700; color: var(--text-primary); margin-bottom: 4px; }
        .detail-grid.detail-grid { display: grid; }
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
                    <div class="version-info">NexJob</div>
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
