using NexJob.Storage;

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
            --space-xs:    4px;
            --space-sm:    8px;
            --space-md:    12px;
            --space-lg:    16px;
            --space-xl:    24px;
            --space-2xl:   32px;
            --radius-sm:   6px;
            --radius-md:   10px;
            --radius-lg:   12px;
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
            justify-content: space-between; margin-bottom: 32px;
            gap: 16px; flex-wrap: wrap;
        }
        .page-header-actions { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
        .page-title { font-size: 24px; font-weight: 800; letter-spacing: -.5px; color: var(--text); }
        .page-subtitle { font-size: 13px; color: var(--text-3); margin-top: 4px; font-weight: 400; }
        .section-title {
            font-size: 10px; font-weight: 700; letter-spacing: .12em;
            text-transform: uppercase; color: var(--text-3); margin-bottom: 12px;
        }

        /* Metric cards */
        .cards { display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; margin-bottom: 32px; }
        .card {
            background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-lg);
            padding: 20px 18px;
            box-shadow: 0 1px 3px rgba(0,0,0,.4), inset 0 1px 0 rgba(255,255,255,.04);
            transition: all .15s ease;
            position: relative; overflow: hidden;
        }
        .card::before {
            content: ''; position: absolute; inset: 0;
            background: linear-gradient(135deg, rgba(255,255,255,.01) 0%, transparent 100%);
            pointer-events: none;
        }
        .card:hover {
            border-color: var(--border-hover);
            box-shadow: 0 2px 8px rgba(0,0,0,.5), 0 0 0 1px var(--border-hover),
                        inset 0 1px 0 rgba(255,255,255,.06);
        }
        .card-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px; }
        .card-label {
            display: flex; align-items: center; gap: 6px;
            font-size: 10px; font-weight: 700; text-transform: uppercase;
            letter-spacing: .1em; color: var(--text-3);
        }
        .card-value { font-size: 36px; font-weight: 800; letter-spacing: -1.2px; line-height: 1; margin-top: 4px; }
        .card-delta { font-size: 11px; color: var(--text-3); margin-top: 8px; }
        .card-enqueued   { border-top: 2px solid var(--info); }
        .card-processing { border-top: 2px solid var(--warning); }
        .card-succeeded  { border-top: 2px solid var(--success); }
        .card-failed     { border-top: 2px solid var(--danger); }
        .card-scheduled  { border-top: 2px solid var(--accent-light); }
        .card-expired    { border-top: 2px solid rgba(148,163,184,.4); }
        .card-servers    { border-top: 2px solid var(--success); }
        .card-queues     { border-top: 2px solid var(--accent); }
        .card-recurring  { border-top: 2px solid var(--text-3); }
        .card-enqueued   .card-value { color: var(--info); }
        .card-processing .card-value { color: var(--warning); }
        .card-succeeded  .card-value { color: var(--success); }
        .card-failed     .card-value { color: var(--danger); }
        .card-scheduled  .card-value { color: var(--accent-light); }
        .card-expired    .card-value { color: var(--text-2); }
        .card-servers    .card-value { color: var(--success); }
        .card-queues     .card-value { color: var(--accent-light); }
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
        .dot-expired    { background: rgba(148,163,184,.5); }
        .dot-default    { background: var(--text-3); }
        @keyframes pulse { 0%,100%{opacity:1} 50%{opacity:.35} }

        /* Badges */
        .badge {
            display: inline-flex; align-items: center; gap: 4px;
            padding: 3px 9px; border-radius: 5px;
            font-size: 11px; font-weight: 700; line-height: 1.4; letter-spacing: .02em;
        }
        .badge-enqueued   { background: var(--info-bg);    color: var(--info); }
        .badge-processing { background: var(--warning-bg); color: var(--warning); }
        .badge-succeeded  { background: var(--success-bg); color: var(--success); }
        .badge-failed     { background: var(--danger-bg);  color: var(--danger); }
        .badge-scheduled  { background: var(--accent-glow);color: var(--accent-light); }
        .badge-warning    { background: rgba(251,191,36,.15); color: var(--warning); }
        .badge-awaiting   { background: rgba(148,163,184,.08); color: var(--text-2); }
        .badge-deleted    { background: rgba(71,85,105,.12); color: var(--text-3); }

        /* Tables */
        .section {
            margin-bottom: 28px;
            background: var(--surface);
            border: 1px solid var(--border);
            border-radius: var(--radius-lg);
            padding: 20px;
            box-shadow: 0 1px 3px rgba(0,0,0,.2), inset 0 1px 0 rgba(255,255,255,.02);
        }
        table { width: 100%; border-collapse: collapse; background: var(--surface); border-radius: var(--radius-md); overflow: hidden; border: 1px solid var(--border); }
        th {
            background: linear-gradient(to right, rgba(99,102,241,.08), transparent);
            padding: 12px 14px; text-align: left; font-size: 10px; text-transform: uppercase;
            letter-spacing: .1em; color: var(--text-3); border-bottom: 1px solid var(--border);
            font-weight: 700;
        }
        td { padding: 12px 14px; border-bottom: 1px solid var(--border); vertical-align: middle; }
        tr:last-child td { border-bottom: none; }
        tr:hover td { background: rgba(99,102,241,.04); }

        /* Job rows */
        .job-list { display: flex; flex-direction: column; gap: 8px; }
        .job-row {
            display: grid; grid-template-columns: 8px 1fr auto;
            gap: 0 16px; padding: 14px 16px;
            background: var(--surface); border: 1px solid var(--border);
            border-radius: var(--radius-md); cursor: pointer;
            transition: border-color .12s, background .12s, box-shadow .15s; align-items: start;
            box-shadow: 0 1px 2px rgba(0,0,0,.2);
        }
        .job-row:hover {
            border-color: var(--border-hover); background: var(--surface2);
            box-shadow: 0 2px 6px rgba(0,0,0,.3);
        }
        .job-row-dot { margin-top: 3px; }
        .job-row-main { min-width: 0; }
        .job-row-meta { text-align: right; font-size: 11px; color: var(--text-3); white-space: nowrap; }
        .job-row-title { font-size: 14px; font-weight: 600; color: var(--text); margin-bottom: 4px; letter-spacing: -.3px; }
        .job-row-sub { font-size: 12px; color: var(--text-3); display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
        .job-row-tags { display: flex; gap: 6px; flex-wrap: wrap; margin-top: 8px; }

        /* Buttons */
        .btn {
            display: inline-flex; align-items: center; gap: 6px;
            padding: 7px 15px; border-radius: var(--radius-sm);
            font-size: 12px; font-weight: 600; cursor: pointer;
            border: 1px solid transparent; text-align: center;
            transition: all .12s ease; line-height: 1; letter-spacing: .02em;
        }
        .btn-primary {
            background: var(--accent); color: #fff; border-color: var(--accent);
            box-shadow: 0 2px 4px rgba(99,102,241,.3);
        }
        .btn-primary:hover {
            background: #4f51d4; border-color: #4f51d4; text-decoration: none; color: #fff;
            box-shadow: 0 4px 8px rgba(99,102,241,.4);
        }
        .btn-danger  { background: var(--danger-bg); color: var(--danger); border-color: rgba(248,113,113,.2); }
        .btn-danger:hover { background: rgba(248,113,113,.18); }
        .btn-ghost   { background: rgba(255,255,255,.04); color: var(--text-2); border-color: var(--border); }
        .btn-ghost:hover { border-color: var(--border-hover); color: var(--text); background: rgba(255,255,255,.06); }
        .btn-sm { padding: 5px 11px; font-size: 11px; }

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
            border-radius: var(--radius-lg); padding: 24px; margin-bottom: 32px; position: relative;
            box-shadow: 0 1px 3px rgba(0,0,0,.2), inset 0 1px 0 rgba(255,255,255,.02);
        }
        .chart-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 20px; }
        .bars { display: flex; align-items: flex-end; gap: 2px; height: 140px; position: relative; padding-bottom: 24px; }
        .bar-wrap { flex: 1; display: flex; flex-direction: column; align-items: center; position: relative; height: 100%; justify-content: flex-end; }
        .bar {
            width: 100%; border-radius: 2px 2px 0 0; min-height: 1px;
            background: linear-gradient(to bottom, var(--accent), rgba(99,102,241,.3));
            cursor: pointer; transition: opacity .15s; position: relative;
        }
        .bar:hover { opacity: .8; }
        .bar-label { font-size: 9px; color: var(--text-3); position: absolute; bottom: -18px; white-space: nowrap; }
        .chart-tooltip {
            display: none; position: fixed;
            background: var(--surface3); border: 1px solid var(--border-hover);
            border-radius: var(--radius-sm); padding: 8px 12px; font-size: 12px; color: var(--text);
            pointer-events: none; z-index: 100; white-space: nowrap;
            box-shadow: 0 8px 24px rgba(0,0,0,.5);
        }

        /* Detail grid (grouped sections) */
        .detail-sections { display: flex; flex-direction: column; gap: 18px; margin-bottom: 28px; }
        .detail-section {
            background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-lg); overflow: hidden;
            box-shadow: 0 1px 3px rgba(0,0,0,.2), inset 0 1px 0 rgba(255,255,255,.02);
        }
        .detail-section-header {
            padding: 12px 16px; background: linear-gradient(to right, rgba(99,102,241,.05), transparent);
            border-bottom: 1px solid var(--border);
            font-size: 10px; font-weight: 700; letter-spacing: .12em; text-transform: uppercase; color: var(--text-3);
        }
        .detail-grid { display: grid; grid-template-columns: 160px 1fr; gap: 0; }
        .detail-label {
            padding: 12px 16px; color: var(--text-3); font-size: 11px; border-bottom: 1px solid var(--border);
            font-weight: 600; text-transform: uppercase; letter-spacing: .05em; background: rgba(0,0,0,.1);
        }
        .detail-value {
            padding: 12px 16px; word-break: break-all; font-size: 13px; border-bottom: 1px solid var(--border);
            color: var(--text);
        }
        .detail-label:last-of-type, .detail-value:last-of-type { border-bottom: none; }

        /* Code & pre */
        pre {
            background: var(--surface2); border: 1px solid var(--border); border-radius: var(--radius-md);
            padding: 14px 16px; overflow-x: auto; font-size: 12px; line-height: 1.6;
            white-space: pre-wrap; word-break: break-word;
            font-family: 'JetBrains Mono', 'Fira Code', ui-monospace, monospace;
            color: var(--text);
        }
        .jk { color: #94a3b8; font-weight: 500; }
        .js { color: #a5b4fc; }
        .jn { color: #34d399; font-weight: 500; }
        .jb { color: #fbbf24; font-weight: 500; }

        /* Log terminal */
        .log-terminal {
            background: #020207; border: 1px solid rgba(255,255,255,.06); border-radius: var(--radius-md);
            padding: 14px 16px; overflow-x: auto; font-size: 12px; line-height: 1.7;
            word-break: break-word;
            font-family: 'JetBrains Mono', 'Fira Code', ui-monospace, monospace;
            color: #e5e7eb;
        }

        /* Progress bar */
        .progress-wrap {
            margin: 16px 0; padding: 16px;
            background: var(--surface); border: 1px solid var(--border);
            border-radius: var(--radius-lg); box-shadow: 0 1px 3px rgba(0,0,0,.2);
        }
        .progress-bar-track { background: var(--surface2); border-radius: 999px; height: 10px; overflow: hidden; }
        .progress-bar-fill {
            height: 100%; background: linear-gradient(to right, var(--accent), var(--accent-light));
            border-radius: 999px; transition: width .5s ease;
            box-shadow: 0 0 12px rgba(99,102,241,.4);
        }
        .progress-info { display: flex; align-items: center; gap: 8px; margin-top: 10px; font-size: 12px; color: var(--text-2); }
        .progress-pct { font-weight: 700; color: var(--accent-light); }

        /* Tag badges */
        .tag-badge {
            display: inline-flex; align-items: center; padding: 3px 10px; border-radius: 5px;
            font-size: 11px; font-weight: 700;
            background: var(--accent-glow); color: var(--accent-light);
            border: 1px solid rgba(99,102,241,.3); white-space: nowrap; transition: all .12s ease;
            letter-spacing: .02em;
        }
        .tag-badge:hover { background: rgba(99,102,241,.3); text-decoration: none; color: var(--accent-light); }

        /* Queue cards */
        .queue-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 16px; }
        .queue-card {
            background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-lg); padding: 20px;
            box-shadow: 0 1px 3px rgba(0,0,0,.4), inset 0 1px 0 rgba(255,255,255,.04);
            transition: all .15s ease;
            position: relative; overflow: hidden;
        }
        .queue-card::before {
            content: ''; position: absolute; inset: 0;
            background: linear-gradient(135deg, rgba(255,255,255,.01) 0%, transparent 100%);
            pointer-events: none;
        }
        .queue-card:hover {
            border-color: var(--border-hover);
            box-shadow: 0 2px 8px rgba(0,0,0,.5), 0 0 0 1px var(--border-hover);
        }
        .queue-card-header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 16px; gap: 12px; }
        .queue-name { font-size: 15px; font-weight: 700; color: var(--text); letter-spacing: -.3px; }
        .queue-metrics { display: flex; gap: 28px; margin-bottom: 16px; }
        .queue-metric-label { font-size: 10px; text-transform: uppercase; letter-spacing: .1em; color: var(--text-3); font-weight: 700; margin-bottom: 4px; }
        .queue-metric-val { font-size: 24px; font-weight: 800; letter-spacing: -.6px; }
        .queue-util-bar { background: var(--surface2); border-radius: 999px; height: 7px; overflow: hidden; margin: 12px 0; }
        .queue-util-fill { height: 100%; background: linear-gradient(to right, var(--accent), var(--accent-light)); border-radius: 999px; }
        .queue-util-label { font-size: 11px; color: var(--text-3); }

        /* Recurring cards */
        .recurring-list { display: flex; flex-direction: column; gap: 10px; }
        .recurring-card {
            background: var(--surface); border: 1px solid var(--border);
            border-radius: var(--radius-lg); padding: 16px 18px; transition: border-color .12s, box-shadow .15s;
            box-shadow: 0 1px 2px rgba(0,0,0,.2);
        }
        .recurring-card:hover {
            border-color: var(--border-hover);
            box-shadow: 0 2px 6px rgba(0,0,0,.3);
        }
        .recurring-card-header { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
        .recurring-card-left { display: flex; align-items: center; gap: 8px; min-width: 0; }
        .recurring-card-right { display: flex; align-items: center; gap: 6px; flex-shrink: 0; }
        .recurring-card-meta { font-size: 12px; color: var(--text-3); margin-top: 10px; display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
        .recurring-id { font-size: 14px; font-weight: 700; color: var(--text); letter-spacing: -.3px; }
        code.cron {
            font-family: 'JetBrains Mono', ui-monospace, monospace; font-size: 11px;
            background: var(--surface2); padding: 3px 8px; border-radius: 4px; color: var(--warning);
            border: 1px solid var(--border); font-weight: 500;
        }

        /* Settings */
        .settings-card {
            background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-lg);
            overflow: hidden; margin-bottom: 16px; box-shadow: 0 1px 3px rgba(0,0,0,.2);
        }
        .settings-card-header {
            padding: 14px 16px; background: linear-gradient(to right, rgba(99,102,241,.05), transparent);
            border-bottom: 1px solid var(--border); font-size: 10px; font-weight: 700;
            letter-spacing: .12em; text-transform: uppercase; color: var(--text-3);
        }
        .settings-card-body { padding: 4px 0; }
        .settings-row { display: flex; align-items: center; gap: 12px; padding: 12px 16px; border-bottom: 1px solid var(--border); }
        .settings-row:last-child { border-bottom: none; }
        .settings-row-label { flex: 1; font-size: 13px; color: var(--text); font-weight: 500; }
        .settings-row-sub { font-size: 11px; color: var(--text-3); margin-top: 2px; }

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

        /* Timeline */
        .timeline-section { margin-bottom: 28px; background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius-lg); padding: 20px; box-shadow: 0 1px 3px rgba(0,0,0,.2), inset 0 1px 0 rgba(255,255,255,.02); }
        .timeline { position: relative; padding: 8px 0; }
        .timeline-item { display: flex; align-items: flex-start; gap: 16px; position: relative; }
        .timeline-item:not(:last-child) { margin-bottom: 22px; }
        .timeline-node { width: 12px; height: 12px; border-radius: 50%; flex-shrink: 0; margin-top: 5px; border: 2px solid var(--border); transition: all .2s ease; }
        .timeline-node:hover { transform: scale(1.25); }
        .timeline-node-neutral { background: var(--text-3); border-color: var(--text-3); }
        .timeline-node-active { background: var(--warning); border-color: var(--warning); box-shadow: 0 0 10px rgba(251,191,36,.5); animation: timeline-pulse 2s cubic-bezier(0.4, 0, 0.6, 1) infinite; }
        .timeline-node-success { background: var(--success); border-color: var(--success); box-shadow: 0 0 8px rgba(52,211,153,.4); }
        .timeline-node-error { background: var(--danger); border-color: var(--danger); box-shadow: 0 0 8px rgba(248,113,113,.4); }
        .timeline-node-warning { background: var(--warning); border-color: var(--warning); box-shadow: 0 0 8px rgba(251,191,36,.3); }
        .timeline-node-muted { background: var(--text-3); border-color: var(--text-3); opacity: .5; }
        .timeline-node-final { width: 16px; height: 16px; margin-top: 2px; box-shadow: 0 0 16px currentColor, inset 0 0 4px rgba(255,255,255,.1); }
        .timeline-node-final.timeline-node-success { box-shadow: 0 0 16px rgba(52,211,153,.6), inset 0 0 4px rgba(255,255,255,.1); }
        .timeline-node-final.timeline-node-error { box-shadow: 0 0 16px rgba(248,113,113,.6), inset 0 0 4px rgba(255,255,255,.1); }
        .timeline-node-final.timeline-node-warning { box-shadow: 0 0 16px rgba(251,191,36,.5), inset 0 0 4px rgba(255,255,255,.1); }
        .timeline-node-final.timeline-node-muted { box-shadow: 0 0 10px rgba(71,85,105,.4), inset 0 0 4px rgba(255,255,255,.05); }
        .timeline-content { flex: 1; min-width: 0; }
        .timeline-label { font-size: 13px; font-weight: 600; color: var(--text); }
        .timeline-metadata { font-size: 11px; color: var(--text-3); margin-top: 2px; font-style: italic; }
        .timeline-time { font-size: 12px; color: var(--text-2); font-family: monospace; margin-top: 2px; }
        .timeline-relative { font-size: 11px; color: var(--text-3); margin-top: 2px; }
        .timeline-line { position: absolute; left: 5px; top: 17px; width: 2px; height: 22px; background: linear-gradient(to bottom, var(--border), transparent); }
        @keyframes timeline-pulse { 0%, 100% { box-shadow: 0 0 10px rgba(251,191,36,.5); } 50% { box-shadow: 0 0 20px rgba(251,191,36,.8); } }

        /* Pagination */
        .pagination { display: flex; gap: 6px; margin-top: 16px; align-items: center; }
        .page-info { color: var(--text-3); font-size: 12px; margin-left: 6px; }

        /* Alert banner */
        .alert { border-radius: 8px; padding: 10px 14px; font-size: 12px; margin-bottom: 16px; display: flex; align-items: center; gap: 8px; }
        .alert-warning { background: var(--warning-bg); border: 1px solid rgba(251,191,36,.2); color: var(--warning); }
        .alert-danger  { background: var(--danger-bg);  border: 1px solid rgba(248,113,113,.2); color: var(--danger); }

        /* Responsive */
        @media (max-width: 1024px) {
            .cards { grid-template-columns: repeat(2, 1fr); gap: 14px; }
            .content { padding: 24px 28px; max-width: 100%; }
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
            .cards { grid-template-columns: repeat(2, 1fr); gap: 12px; }
            .content { padding: 16px; }
            .page-title { font-size: 20px; }
            .queue-grid { grid-template-columns: 1fr; }
            .detail-grid { grid-template-columns: 140px 1fr; }
        }

        /* Health badge */
        .health-badge {
            margin: 8px 10px; padding: 5px 10px;
            border-radius: var(--radius-sm);
            display: flex; align-items: center; gap: 6px;
            font-size: 11px; font-weight: 600; letter-spacing: .04em;
        }
        .health-badge.healthy  { background: var(--success-bg); color: var(--success); }
        .health-badge.degraded { background: var(--warning-bg); color: var(--warning); }
        .health-badge.incident  { background: var(--danger-bg);  color: var(--danger);  }
        .health-pulse { width: 6px; height: 6px; border-radius: 50%; flex-shrink: 0; }
        .healthy  .health-pulse { background: var(--success); }
        .degraded .health-pulse { background: var(--warning); }
        .incident .health-pulse { background: var(--danger); animation: hpulse 1s infinite; }
        @keyframes hpulse { 0%,100%{opacity:1} 50%{opacity:.2} }

        /* Nav counters */
        .nav-counter {
            font-size: 10px; font-family: monospace;
            padding: 1px 5px; border-radius: 4px;
            margin-left: auto; flex-shrink: 0;
            background: rgba(255,255,255,.06); color: var(--text-3);
            border: 1px solid var(--border);
        }
        .nav-counter.warn   { background: var(--warning-bg); color: var(--warning); border-color: transparent; }
        .nav-counter.danger { background: var(--danger-bg);  color: var(--danger);  border-color: transparent; }
        .nav-counter.ok     { background: var(--success-bg); color: var(--success); border-color: transparent; }
        """;

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
                {{HealthBadge(metrics)}}
                <div class="nav-section">
                    <ul class="nav-list">
                        <li><a href="{{pathPrefix}}" class="nav-link {{Active(activeRoute, "overview")}}">
                            <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5"><rect x="1" y="1" width="6" height="6" rx="1"/><rect x="9" y="1" width="6" height="6" rx="1"/><rect x="1" y="9" width="6" height="6" rx="1"/><rect x="9" y="9" width="6" height="6" rx="1"/></svg>
                            <span class="nav-label">Overview</span></a></li>
                        <li><a href="{{pathPrefix}}/queues" class="nav-link {{Active(activeRoute, "queues")}}">
                            <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5"><rect x="1" y="10" width="14" height="4" rx="1"/><rect x="1" y="6" width="14" height="3" rx="1" opacity=".6"/><rect x="1" y="2" width="14" height="3" rx="1" opacity=".35"/></svg>
                            <span class="nav-label">Queues</span>{{NavCounter(counters?.Queues, counters?.QueuesClass)}}</a></li>
                        <li><a href="{{pathPrefix}}/servers" class="nav-link {{Active(activeRoute, "servers")}}">
                            <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5"><rect x="2" y="2" width="12" height="4" rx="1"/><rect x="2" y="10" width="12" height="4" rx="1"/><line x1="5" y1="4" x2="5.01" y2="4"/><line x1="5" y1="12" x2="5.01" y2="12"/></svg>
                            <span class="nav-label">Servers</span>{{NavCounter(counters?.Servers, counters?.ServersClass)}}</a></li>
                        <li><a href="{{pathPrefix}}/jobs" class="nav-link {{Active(activeRoute, "jobs")}}">
                            <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5"><line x1="3" y1="4" x2="13" y2="4"/><line x1="3" y1="8" x2="13" y2="8"/><line x1="3" y1="12" x2="9" y2="12"/></svg>
                            <span class="nav-label">Jobs</span>{{NavCounter(counters?.Jobs, null)}}</a></li>
                        <li><a href="{{pathPrefix}}/recurring" class="nav-link {{Active(activeRoute, "recurring")}}">
                            <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M13.5 8A5.5 5.5 0 1 1 8 2.5"/><polyline points="11,1 14,2.5 11,4"/></svg>
                            <span class="nav-label">Recurring</span>{{NavCounter(counters?.Recurring, null)}}</a></li>
                        <li><a href="{{pathPrefix}}/failed" class="nav-link {{Active(activeRoute, "failed")}}">
                            <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.5"><circle cx="8" cy="8" r="6.5"/><line x1="5.5" y1="5.5" x2="10.5" y2="10.5"/><line x1="10.5" y1="5.5" x2="5.5" y2="10.5"/></svg>
                            <span class="nav-label">Failed</span>{{NavCounter(counters?.Failed, counters?.FailedClass)}}</a></li>
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
        <script>
        (function(){
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

    internal static string NotFound(string title, string pathPrefix) =>
        Wrap(title, pathPrefix, string.Empty,
            "<div class=\"empty-state\"><svg width=\"48\" height=\"48\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1\"><circle cx=\"12\" cy=\"12\" r=\"10\"/><line x1=\"12\" y1=\"8\" x2=\"12\" y2=\"12\"/><line x1=\"12\" y1=\"16\" x2=\"12.01\" y2=\"16\"/></svg><p>404 — Page not found</p></div>");

    private static string Active(string route, string page) =>
        route == page ? "active" : string.Empty;

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
