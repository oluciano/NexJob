using NexJob.Storage;

namespace NexJob.Dashboard;

/// <summary>Shared HTML shell (layout wrapper) injected around page component output.</summary>
internal static class HtmlShell
{
    private const string Css =
        """
        :root {
            /* Gray scale — neutral */
            --gray-0:   255 255 255;
            --gray-50:  250 250 250;
            --gray-100: 241 241 241;
            --gray-200: 227 227 227;
            --gray-300: 196 196 196;
            --gray-400: 146 146 146;
            --gray-500: 102 102 102;
            --gray-600:  72  72  72;
            --gray-700:  51  51  51;
            --gray-800:  34  34  34;
            --gray-900:  17  17  17;
            --gray-1000:  0   0   0;

            /* Semantic */
            --background: 255 255 255;
            --foreground:  72  72  72;
            --muted:      227 227 227;
            --muted-fg:   146 146 146;

            /* Primary (near-black on light, near-white on dark) */
            --primary:           17  17  17;
            --primary-fg:       255 255 255;
            --primary-lighter:  227 227 227;

            /* Status colors */
            --green:   17 168  73;
            --green-lighter: 185 249 207;
            --orange: 245 166  35;
            --orange-lighter: 255 239 207;
            --red:    238   0   0;
            --red-lighter: 247 212 214;
            --blue:     0 112 243;
            --blue-lighter: 211 229 255;

            /* Spacing */
            --radius-sm:  6px;
            --radius-md: 10px;
            --radius-lg: 12px;

            /* Compatibility mappings */
            --bg:            rgb(var(--background) / 1);
            --surface:       rgb(var(--gray-0) / 1);
            --surface2:      rgb(var(--gray-50) / 1);
            --surface3:      rgb(var(--gray-100) / 1);
            --accent:        rgb(var(--primary) / 1);
            --accent-glow:   rgb(var(--primary-lighter) / 1);
            --accent-light:  rgb(var(--blue) / 1);
            --border:        rgb(var(--muted) / 1);
            --border-hover:  rgb(var(--gray-300) / 1);
            --text:          rgb(var(--foreground) / 1);
            --text-2:        rgb(var(--gray-500) / 1);
            --text-3:        rgb(var(--gray-400) / 1);
            --text-muted:    rgb(var(--gray-400) / 1);
            --success:       rgb(var(--green) / 1);
            --warning:       rgb(var(--orange) / 1);
            --danger:        rgb(var(--red) / 1);
            --info:          rgb(var(--blue) / 1);
            --success-bg:    rgb(var(--green-lighter) / 1);
            --warning-bg:    rgb(var(--orange-lighter) / 1);
            --danger-bg:     rgb(var(--red-lighter) / 1);
            --info-bg:       rgb(var(--blue-lighter) / 1);
        }

        [data-theme="dark"] {
            --gray-0:     0   0   0;
            --gray-50:   17  17  17;
            --gray-100:  31  31  31;
            --gray-200:  51  51  51;
            --gray-300:  72  72  72;
            --gray-400: 102 102 102;
            --gray-500: 146 146 146;
            --gray-600: 162 162 162;
            --gray-700: 196 196 196;
            --gray-800: 223 223 223;
            --gray-900: 241 241 241;
            --gray-1000:255 255 255;

            --background:  8   9  14;
            --foreground: 223 223 223;
            --muted:       51  51  51;
            --muted-fg:   102 102 102;

            --primary:          241 241 241;
            --primary-fg:         0   0   0;
            --primary-lighter:   34  34  34;
            --green-lighter:      3  48  22;
            --orange-lighter:    68  29   4;
            --red-lighter:       80   0   0;
            --blue-lighter:      13  51  94;
        }

        *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
            background: rgb(var(--background) / 1); color: rgb(var(--foreground) / 1);
            font-size: 13px; line-height: 1.6;
            -webkit-font-smoothing: antialiased;
        }
        a { color: rgb(var(--blue) / 1); text-decoration: none; }
        a:hover { text-decoration: underline; }

        /* Layout */
        .layout { display: flex; min-height: 100vh; }
        .sidebar {
            width: 240px; background: rgb(var(--gray-50) / 1);
            border-right: 1px solid rgb(var(--muted) / 1); flex-shrink: 0;
            display: flex; flex-direction: column;
            position: sticky; top: 0; height: 100vh; overflow-y: auto;
        }
        .sidebar-header {
            padding: 24px 20px 20px;
            display: flex; align-items: center; gap: 10px;
        }
        .logo-text { font-size: 15px; font-weight: 700; color: rgb(var(--gray-900) / 1); letter-spacing: -0.02em; }
        .nav-section { padding: 8px 0; flex: 1; }
        .nav-list { list-style: none; padding: 0 12px; }
        .nav-link {
            display: flex; align-items: center; gap: 10px;
            padding: 8px 12px; border-radius: var(--radius-sm);
            color: rgb(var(--gray-500) / 1); font-size: 13px; font-weight: 500;
            transition: all .15s;
            border-left: 2px solid transparent;
        }
        .nav-link:hover { background: rgb(var(--gray-100) / 1); color: rgb(var(--gray-900) / 1); text-decoration: none; }
        .nav-link.active {
            background: rgb(var(--primary-lighter) / 1); color: rgb(var(--primary) / 1);
            border-left-color: rgb(var(--primary) / 1);
        }
        .nav-link svg { flex-shrink: 0; opacity: .7; }
        .nav-link.active svg { opacity: 1; }
        .sidebar-footer {
            padding: 16px 20px; border-top: 1px solid rgb(var(--muted) / 1);
            font-size: 11px; color: rgb(var(--gray-400) / 1);
            display: flex; justify-content: space-between; align-items: center;
        }
        #theme-toggle {
            background: none; border: none; cursor: pointer; font-size: 14px;
            padding: 4px; border-radius: 4px; transition: background 0.2s;
        }
        #theme-toggle:hover { background: rgb(var(--gray-200) / 1); }

        .content { flex: 1; padding: 32px 36px; max-width: 1280px; margin: 0 auto; width: 100%; }

        /* Page header */
        .page-header {
            display: flex; align-items: flex-start;
            justify-content: space-between; margin-bottom: 32px;
            gap: 16px;
        }
        .page-title { font-size: 24px; font-weight: 700; color: rgb(var(--gray-900) / 1); letter-spacing: -0.02em; }
        .page-subtitle { font-size: 13px; color: rgb(var(--muted-fg) / 1); margin-top: 4px; }

        /* Metric cards */
        .cards { display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; margin-bottom: 32px; }
        .card {
            background: rgb(var(--gray-0) / 1); border: 1px solid rgb(var(--muted) / 1); border-radius: var(--radius-lg);
            padding: 20px 18px;
            box-shadow: 0 1px 2px rgba(0,0,0,.05);
            transition: all .2s ease;
            position: relative;
        }
        .card:hover {
            border-color: rgb(var(--gray-300) / 1);
            box-shadow: 0 4px 12px rgba(0,0,0,.08);
            transform: translateY(-1px);
        }
        .card-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px; }
        .card-label {
            display: flex; align-items: center; gap: 8px;
            font-size: 11px; font-weight: 600; text-transform: uppercase;
            letter-spacing: .05em; color: rgb(var(--gray-500) / 1);
        }
        .card-value { font-size: 32px; font-weight: 700; letter-spacing: -0.02em; line-height: 1; }
        .card-delta { font-size: 11px; color: rgb(var(--muted-fg) / 1); margin-top: 8px; }

        .card-enqueued   { border-top: 2px solid rgb(var(--blue) / 1); }
        .card-processing { border-top: 2px solid rgb(var(--orange) / 1); }
        .card-succeeded  { border-top: 2px solid rgb(var(--green) / 1); }
        .card-failed     { border-top: 2px solid rgb(var(--red) / 1); }
        .card-scheduled  { border-top: 2px solid rgb(var(--gray-400) / 1); }
        .card-expired    { border-top: 2px solid rgb(var(--gray-300) / 1); }

        .card-enqueued   .card-value { color: rgb(var(--blue) / 1); }
        .card-processing .card-value { color: rgb(var(--orange) / 1); }
        .card-succeeded  .card-value { color: rgb(var(--green) / 1); }
        .card-failed     .card-value { color: rgb(var(--red) / 1); }

        /* Badges */
        .badge {
            display: inline-flex; align-items: center; justify-content: center;
            padding: 2px 10px; border-radius: 9999px;
            font-size: 11px; font-weight: 600;
            text-transform: capitalize; letter-spacing: .02em;
            border: 1px solid;
        }
        .badge-green  { background: rgb(var(--green-lighter)/1); color: rgb(var(--green)/1); border-color: rgb(var(--green)/0.3); }
        .badge-orange { background: rgb(var(--orange-lighter)/1); color: rgb(var(--orange)/1); border-color: rgb(var(--orange)/0.3); }
        .badge-red    { background: rgb(var(--red-lighter)/1); color: rgb(var(--red)/1); border-color: rgb(var(--red)/0.3); }
        .badge-blue   { background: rgb(var(--blue-lighter)/1); color: rgb(var(--blue)/1); border-color: rgb(var(--blue)/0.3); }
        .badge-gray   { background: rgb(var(--gray-100)/1); color: rgb(var(--gray-500)/1); border-color: rgb(var(--muted)/1); }

        .card-badge { font-size: 10px; padding: 1px 8px; }

        /* Tables */
        .table-wrapper { overflow-x: auto; border: 1px solid rgb(var(--muted)/1); border-radius: var(--radius-md); background: rgb(var(--gray-0)/1); }
        table { width: 100%; border-collapse: collapse; font-size: 13px; }
        thead th {
            background: rgb(var(--gray-50)/1); padding: 12px 14px;
            font-size: 11px; font-weight: 600; text-transform: uppercase;
            letter-spacing: .05em; color: rgb(var(--gray-500)/1);
            border-bottom: 1px solid rgb(var(--muted)/1); text-align: left;
        }
        tbody tr { border-bottom: 1px solid rgb(var(--muted)/0.5); transition: background 0.1s; }
        tbody tr:last-child { border-bottom: none; }
        tbody tr:hover { background: rgb(var(--gray-50)/1); }
        tbody td { padding: 12px 14px; color: rgb(var(--foreground)/1); vertical-align: middle; }

        /* Dots */
        .dot {
            display: inline-block; width: 8px; height: 8px; border-radius: 50%;
            vertical-align: middle; margin-right: 8px;
        }
        .dot-processing { background: rgb(var(--orange)/1); box-shadow: 0 0 8px rgb(var(--orange)/0.4); animation: pulse 2s infinite; }
        .dot-succeeded  { background: rgb(var(--green)/1); }
        .dot-failed     { background: rgb(var(--red)/1); }
        .dot-enqueued   { background: rgb(var(--blue)/1); }
        .dot-scheduled  { background: rgb(var(--gray-400)/1); }
        .dot-awaiting   { background: rgb(var(--gray-300)/1); }
        .dot-expired    { background: rgb(var(--gray-300)/1); }
        @keyframes pulse { 0%,100%{opacity:1} 50%{opacity:.4} }

        /* Buttons */
        .btn {
            display: inline-flex; align-items: center; gap: 6px;
            padding: 7px 14px; border-radius: var(--radius-sm);
            font-size: 13px; font-weight: 500; cursor: pointer;
            border: 1px solid; transition: all .15s;
        }
        .btn-primary {
            background: rgb(var(--primary)/1); color: rgb(var(--primary-fg)/1);
            border-color: rgb(var(--primary)/1);
        }
        .btn-primary:hover { opacity: .85; box-shadow: 0 2px 8px rgba(0,0,0,0.1); }
        .btn-outline, .btn-ghost {
            background: transparent; color: rgb(var(--gray-700)/1);
            border-color: rgb(var(--muted)/1);
        }
        .btn-outline:hover, .btn-ghost:hover { background: rgb(var(--gray-100)/1); border-color: rgb(var(--gray-300)/1); }
        .btn-danger {
            background: rgb(var(--red)/1); color: #fff;
            border-color: rgb(var(--red)/1);
        }
        .btn-danger:hover { opacity: .85; }
        .btn-sm { padding: 4px 10px; font-size: 11px; }

        /* Forms & Inputs */
        input[type="text"], select {
            background: rgb(var(--gray-0) / 1); border: 1px solid rgb(var(--muted) / 1);
            border-radius: var(--radius-sm); color: rgb(var(--foreground) / 1);
            padding: 7px 12px; font-size: 13px; transition: border-color .15s;
        }
        input[type="text"]:focus { outline: none; border-color: rgb(var(--blue) / 1); }

        /* Status Pills */
        .status-pills { display: flex; gap: 6px; margin-bottom: 16px; flex-wrap: wrap; }
        .status-pill {
            padding: 6px 14px; border-radius: 9999px; font-size: 12px; font-weight: 500;
            cursor: pointer; border: 1px solid rgb(var(--muted) / 1);
            background: rgb(var(--gray-0) / 1); color: rgb(var(--gray-600) / 1);
            transition: all .15s;
        }
        .status-pill:hover { border-color: rgb(var(--gray-300) / 1); color: rgb(var(--gray-900) / 1); }
        .status-pill.active { background: rgb(var(--primary) / 1); color: rgb(var(--primary-fg) / 1); border-color: rgb(var(--primary) / 1); }

        /* Chart */
        .chart {
            background: rgb(var(--gray-0) / 1); border: 1px solid rgb(var(--muted) / 1);
            border-radius: var(--radius-lg); padding: 24px; margin-bottom: 32px;
            box-shadow: 0 1px 2px rgba(0,0,0,.05);
        }
        .section-title { font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: .05em; color: rgb(var(--gray-500) / 1); margin-bottom: 16px; display: block; }
        .bars { display: flex; align-items: flex-end; gap: 4px; height: 140px; padding-bottom: 24px; position: relative; }
        .bar-wrap { flex: 1; display: flex; flex-direction: column; align-items: center; justify-content: flex-end; height: 100%; position: relative; }
        .bar {
            width: 100%; border-radius: 3px 3px 0 0; background: rgb(var(--blue) / 1);
            transition: opacity .15s; cursor: crosshair; min-height: 2px;
        }
        .bar:hover { opacity: .7; }
        .bar.anomaly { background: rgb(var(--red) / 1); }
        .bar-label { position: absolute; bottom: -20px; font-size: 10px; color: rgb(var(--gray-400) / 1); }
        .avg-line { position: absolute; left: 0; right: 0; border-top: 1px dashed rgb(var(--gray-400) / 0.5); pointer-events: none; z-index: 1; }

        /* Job list & rows */
        .section { background: rgb(var(--gray-0) / 1); border: 1px solid rgb(var(--muted) / 1); border-radius: var(--radius-lg); padding: 24px; margin-bottom: 32px; }
        .job-list { display: flex; flex-direction: column; gap: 12px; }
        .job-row {
            display: flex; align-items: center; gap: 16px; padding: 12px 16px;
            border: 1px solid rgb(var(--muted) / 1); border-radius: var(--radius-md);
            transition: all .15s; background: rgb(var(--gray-0) / 1);
        }
        .job-row:hover { border-color: rgb(var(--gray-300) / 1); background: rgb(var(--gray-50) / 1); }
        .job-row-main { flex: 1; min-width: 0; }
        .job-row-title { font-weight: 600; color: rgb(var(--gray-900) / 1); margin-bottom: 2px; }
        .job-row-sub { font-size: 12px; color: rgb(var(--muted-fg) / 1); display: flex; gap: 12px; }
        .job-row-meta { font-size: 12px; color: rgb(var(--gray-500) / 1); text-align: right; }

        /* Queue/Recurring Cards */
        .queue-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 20px; }
        .queue-card, .recurring-card {
            background: rgb(var(--gray-0) / 1); border: 1px solid rgb(var(--muted) / 1); border-radius: var(--radius-lg);
            padding: 24px; transition: all .2s; box-shadow: 0 1px 2px rgba(0,0,0,.05);
        }
        .queue-card:hover, .recurring-card:hover { border-color: rgb(var(--gray-300) / 1); box-shadow: 0 4px 12px rgba(0,0,0,0.08); }
        .queue-name, .recurring-id { font-size: 16px; font-weight: 700; color: rgb(var(--gray-900) / 1); letter-spacing: -0.01em; }
        .queue-metrics { display: flex; gap: 32px; margin: 16px 0; }
        .queue-metric-label { font-size: 11px; font-weight: 600; text-transform: uppercase; color: rgb(var(--gray-400) / 1); margin-bottom: 4px; }
        .queue-metric-val { font-size: 24px; font-weight: 700; }
        .queue-util-bar { background: rgb(var(--gray-100) / 1); height: 8px; border-radius: 4px; overflow: hidden; margin-bottom: 8px; }
        .queue-util-fill { background: rgb(var(--blue) / 1); height: 100%; border-radius: 4px; }

        /* Recurring specific */
        .recurring-card-meta { display: flex; gap: 16px; font-size: 12px; color: rgb(var(--gray-500) / 1); margin: 12px 0; align-items: center; }
        code.cron { background: rgb(var(--gray-100) / 1); padding: 2px 6px; border-radius: 4px; font-family: monospace; font-size: 11px; color: rgb(var(--orange) / 1); }

        /* Progress Bar */
        .progress-wrap { background: rgb(var(--gray-50) / 1); padding: 16px; border-radius: var(--radius-lg); border: 1px solid rgb(var(--muted) / 1); }
        .progress-bar-track { background: rgb(var(--gray-200) / 1); height: 8px; border-radius: 4px; overflow: hidden; }
        .progress-bar-fill { background: rgb(var(--blue) / 1); height: 100%; border-radius: 4px; transition: width 0.3s ease; }
        .progress-info { margin-top: 10px; font-size: 12px; display: flex; gap: 8px; }
        .progress-pct { font-weight: 700; color: rgb(var(--blue) / 1); }

        /* Timeline */
        .timeline { position: relative; padding-left: 20px; }
        .timeline-item { position: relative; padding-bottom: 24px; padding-left: 20px; }
        .timeline-line { position: absolute; left: 3px; top: 12px; bottom: 0; width: 2px; background: rgb(var(--muted) / 1); }
        .timeline-item:last-child .timeline-line { display: none; }
        .timeline-node {
            position: absolute; left: -4px; top: 4px; width: 16px; height: 16px;
            border-radius: 50%; border: 3px solid rgb(var(--background) / 1);
            background: rgb(var(--gray-300) / 1); z-index: 1;
        }
        .timeline-node-active { background: rgb(var(--orange) / 1); box-shadow: 0 0 10px rgb(var(--orange)/0.4); animation: pulse 2s infinite; }
        .timeline-node-success { background: rgb(var(--green) / 1); }
        .timeline-node-error { background: rgb(var(--red) / 1); }
        .timeline-node-warning { background: rgb(var(--orange) / 1); }
        .timeline-node-neutral { background: rgb(var(--gray-400) / 1); }
        .timeline-node-muted { background: rgb(var(--gray-300) / 1); opacity: 0.6; }
        .timeline-node-final { width: 20px; height: 20px; left: -6px; top: 2px; border-width: 4px; }
        .timeline-label { font-weight: 600; color: rgb(var(--gray-900) / 1); }
        .timeline-metadata { font-size: 12px; color: rgb(var(--muted-fg) / 1); }
        .timeline-time { font-size: 11px; color: rgb(var(--gray-400) / 1); margin-top: 2px; }

        /* Responsive */
        @media (max-width: 1024px) {
            .cards { grid-template-columns: repeat(2, 1fr); }
            .queue-grid { grid-template-columns: 1fr; }
        }
        @media (max-width: 768px) {
            .layout { flex-direction: column; }
            .sidebar { width: 100%; height: auto; border-right: none; border-bottom: 1px solid rgb(var(--muted) / 1); position: static; }
            .content { padding: 20px; }
            .cards { grid-template-columns: 1fr; }
        }
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
        <div class="layout">
            <nav class="sidebar">
                <div class="sidebar-header">
                    <svg width="22" height="22" viewBox="0 0 20 20" aria-hidden="true">
                        <polygon points="10,1 18,5.5 18,14.5 10,19 2,14.5 2,5.5"
                                 fill="none" stroke="rgb(var(--primary) / 1)" stroke-width="1.5"/>
                        <circle cx="10" cy="10" r="2.5" fill="rgb(var(--primary) / 1)"/>
                        <line x1="10" y1="7.5" x2="10" y2="3" stroke="rgb(var(--primary) / 1)" stroke-width="1"/>
                        <line x1="12.2" y1="11.3" x2="15.6" y2="13.2" stroke="rgb(var(--primary) / 1)" stroke-width="1"/>
                        <line x1="7.8" y1="11.3" x2="4.4" y2="13.2" stroke="rgb(var(--primary) / 1)" stroke-width="1"/>
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
                    <button id="theme-toggle" title="Toggle dark mode">🌙</button>
                    <a href="https://github.com/oluciano/NexJob" style="color:rgb(var(--gray-400)/1);text-decoration:none">GitHub ↗</a>
                </div>
            </nav>
            <main class="content">
                {{body}}
            </main>
        </div>
        <script>
        (function(){
            // Theme toggle logic
            const t = document.getElementById('theme-toggle');
            const stored = localStorage.getItem('nexjob-theme');
            if (stored === 'dark') {
                document.documentElement.setAttribute('data-theme', 'dark');
                t.textContent = '☀️';
            }
            t.onclick = () => {
                const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
                document.documentElement.setAttribute('data-theme', isDark ? '' : 'dark');
                localStorage.setItem('nexjob-theme', isDark ? 'light' : 'dark');
                t.textContent = isDark ? '🌙' : '☀️';
            };

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
