using System.Diagnostics.CodeAnalysis;
using System.Reflection;
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
            --primary-dark: #009ca0;
            --primary-light: rgba(0, 207, 213, 0.15);
            --secondary: #7367f0;
            --success: #28c76f;
            --success-light: rgba(40, 199, 111, 0.12);
            --warning: #ff9f43;
            --warning-light: rgba(255, 159, 67, 0.12);
            --error: #ea5455;
            --error-light: rgba(234, 84, 85, 0.12);
            --info: #00cfe8;
            --info-light: rgba(0, 207, 213, 0.12);
            --bg-primary: #181f4a;
            --bg-secondary: #0f1535;
            --bg-tertiary: #070c29;
            --header-bg: #181f4a;
            --sidebar-bg: #070c29;
            --sidebar-text: #e2e8f0;
            --sidebar-hover: #181f4a;
            --text-primary: #f8fafc;
            --text-secondary: #94a3b8;
            --text-tertiary: #64748b;
            --border: #232c66;
            --border-light: #232c66;
            --radius: 12px;
            --radius-lg: 14px;
            --transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
            --shadow: 0 4px 20px 0 rgba(0, 0, 0, 0.5);
            --top-header-height: 64px;
            --sidebar-width: 260px;
        }

        [data-theme="light"] {
            --bg-primary: #ffffff;
            --bg-secondary: #f8f7fa;
            --bg-tertiary: #f1f0f5;
            --header-bg: #ffffff;
            --sidebar-bg: #ffffff;
            --sidebar-text: #2f2b3d;
            --sidebar-hover: #f1f0f5;
            --text-primary: #2f2b3d;
            --text-secondary: #6f6b7d;
            --text-tertiary: #b0adba;
            --border: #dbdade;
            --border-light: #eae9ec;
            --shadow: 0 4px 18px 0 rgba(75, 70, 92, 0.08);
        }

        [data-theme="dark"] {
            --bg-primary: #2f3349;
            --bg-secondary: #25293c;
            --bg-tertiary: #161924;
            --header-bg: #2f3349;
            --sidebar-bg: #2f3349;
            --sidebar-text: #cfd3db;
            --sidebar-hover: #161924;
            --text-primary: #cfd3db;
            --text-secondary: #a5a3ae;
            --text-tertiary: #7983bb;
            --border: #43495e;
            --border-light: #43495e;
            --shadow: 0 4px 18px 0 rgba(15, 20, 34, 0.4);
        }

        [data-theme="blue-theme"] {
            --primary: #00cfd5;
            --primary-dark: #009ca0;
            --primary-light: rgba(0, 207, 213, 0.15);
            --bg-primary: #181f4a;
            --bg-secondary: #0f1535;
            --bg-tertiary: #070c29;
            --header-bg: #181f4a;
            --sidebar-bg: #070c29;
            --sidebar-text: #e2e8f0;
            --sidebar-hover: #181f4a;
            --text-primary: #f8fafc;
            --text-secondary: #94a3b8;
            --text-tertiary: #64748b;
            --border: #232c66;
            --shadow: 0 4px 20px 0 rgba(0, 0, 0, 0.5);
        }

        [data-theme="semi-dark"] {
            --bg-primary: #ffffff;
            --bg-secondary: #f4f5f7;
            --bg-tertiary: #ebeef2;
            --header-bg: #1b1e2e;
            --sidebar-bg: #131623;
            --sidebar-text: #cfd3db;
            --sidebar-hover: #1b1e2e;
            --text-primary: #1e293b;
            --text-secondary: #64748b;
            --text-tertiary: #94a3b8;
            --border: #e2e8f0;
            --shadow: 0 4px 18px 0 rgba(75, 70, 92, 0.08);
        }

        [data-theme="bordered-theme"] {
            --bg-primary: #ffffff;
            --bg-secondary: #fbfbfb;
            --bg-tertiary: #f2f2f4;
            --header-bg: #ffffff;
            --sidebar-bg: #ffffff;
            --sidebar-text: #2f2b3d;
            --sidebar-hover: #f2f2f4;
            --text-primary: #2f2b3d;
            --text-secondary: #6f6b7d;
            --text-tertiary: #a19fa8;
            --border: #c8c7ce;
            --shadow: none;
        }

        * { margin: 0; padding: 0; box-sizing: border-box; }
        body {
            font-family: 'Public Sans', -apple-system, sans-serif;
            background: var(--bg-secondary); color: var(--text-primary);
            line-height: 1.5; -webkit-font-smoothing: antialiased;
        }

        /* Top Header */
        .top-header {
            position: fixed; top: 0; left: 0; right: 0; height: var(--top-header-height);
            background: var(--header-bg); border-bottom: 1px solid var(--border);
            display: flex; align-items: center; justify-content: space-between;
            padding: 0 24px; z-index: 1050; box-shadow: var(--shadow);
            transition: var(--transition);
        }
        .header-left { display: flex; align-items: center; gap: 16px; }
        .header-logo { display: flex; align-items: center; gap: 10px; text-decoration: none; color: inherit; width: calc(var(--sidebar-width) - 40px); }
        .header-logo h1 { font-size: 20px; font-weight: 700; color: var(--text-primary); letter-spacing: -0.5px; }
        .logo-badge { font-size: 10px; font-weight: 700; background: var(--primary-light); color: var(--primary); padding: 2px 6px; border-radius: 4px; text-transform: uppercase; }

        .btn-toggle-sidebar {
            background: none; border: none; color: var(--text-secondary); cursor: pointer;
            padding: 8px; border-radius: 6px; display: flex; align-items: center; justify-content: center;
            transition: var(--transition);
        }
        .btn-toggle-sidebar:hover { background: var(--bg-tertiary); color: var(--primary); }

        .header-search {
            display: flex; align-items: center; gap: 8px; background: var(--bg-tertiary);
            padding: 7px 14px; border-radius: 8px; border: 1px solid var(--border);
            width: 320px; transition: var(--transition); cursor: text;
        }
        .header-search:focus-within { border-color: var(--primary); box-shadow: 0 0 0 2px var(--primary-light); }
        .header-search input {
            border: none; background: transparent; outline: none; width: 100%;
            font-size: 13px; color: var(--text-primary); font-family: inherit;
        }
        .header-search kbd {
            font-size: 10px; font-weight: 600; padding: 2px 6px; border-radius: 4px;
            background: var(--bg-primary); border: 1px solid var(--border);
            color: var(--text-tertiary); font-family: inherit;
        }

        .header-right { display: flex; align-items: center; gap: 12px; }
        .header-btn {
            background: var(--bg-tertiary); border: 1px solid var(--border); color: var(--text-secondary);
            cursor: pointer; padding: 8px 12px; border-radius: 8px; display: flex; align-items: center; gap: 6px;
            font-size: 13px; font-weight: 500; text-decoration: none; transition: var(--transition);
        }
        .header-btn:hover { color: var(--primary); border-color: var(--primary); transform: translateY(-1px); }

        /* Container Layout */
        .app-container { display: flex; min-height: 100vh; padding-top: var(--top-header-height); }

        /* Categorized Sidebar */
        .sidebar {
            width: var(--sidebar-width); background: var(--sidebar-bg);
            border-right: 1px solid var(--border);
            display: flex; flex-direction: column;
            position: fixed; top: var(--top-header-height); bottom: 0; left: 0;
            z-index: 100; box-shadow: var(--shadow); transition: var(--transition);
        }
        .sidebar.collapsed { width: 72px; }
        .sidebar.collapsed .nav-category-title,
        .sidebar.collapsed .nav-counter,
        .sidebar.collapsed .sidebar-footer span,
        .sidebar.collapsed .nav-label { display: none; }
        .sidebar.collapsed .nav-item { justify-content: center; padding: 12px; }

        .nav-scroller { flex: 1; padding: 16px 12px; display: flex; flex-direction: column; gap: 6px; overflow-y: auto; }
        .nav-category-title {
            font-size: 10px; font-weight: 700; color: var(--text-tertiary);
            padding: 12px 14px 4px 14px; text-transform: uppercase; letter-spacing: 0.8px;
        }
        .nav-item {
            display: flex; align-items: center; justify-content: space-between;
            padding: 9px 14px; border-radius: 8px;
            color: var(--sidebar-text); text-decoration: none;
            font-size: 14px; font-weight: 500; transition: var(--transition);
        }
        .nav-item-left { display: flex; align-items: center; gap: 10px; }
        .nav-item svg { width: 18px; height: 18px; flex-shrink: 0; opacity: 0.8; }
        .nav-item:hover { background: var(--sidebar-hover); color: var(--primary); transform: translateX(3px); }
        .nav-item.active {
            background: linear-gradient(72.47deg, var(--primary) 22.16%, rgba(0, 207, 213, 0.7) 76.47%);
            color: #fff; box-shadow: 0px 3px 10px rgba(0, 207, 213, 0.35); font-weight: 600;
        }
        .nav-item.active svg { opacity: 1; }

        .nav-counter { font-size: 11px; font-weight: 700; padding: 2px 7px; border-radius: 10px; background: rgba(0,0,0,0.12); color: inherit; }
        .nav-counter.alert { background: var(--error); color: #fff; }

        .sidebar-footer {
            margin-top: auto; padding: 14px 18px; border-top: 1px solid var(--border);
            display: flex; justify-content: space-between; align-items: center; font-size: 11px; color: var(--text-secondary);
        }

        /* Main Content */
        .main-content {
            flex: 1; margin-left: var(--sidebar-width); padding: 32px;
            transition: var(--transition); max-width: calc(100vw - var(--sidebar-width));
        }
        .sidebar.collapsed ~ .main-content {
            margin-left: 72px; max-width: calc(100vw - 72px);
        }

        /* Health Badge */
        .health-badge {
            padding: 5px 12px; border-radius: 20px; font-size: 11px; font-weight: 700;
            display: inline-flex; align-items: center; gap: 6px; letter-spacing: 0.5px;
        }
        .health-badge.healthy { background: var(--success-light); color: var(--success); }
        .health-badge.incident { background: var(--error-light); color: var(--error); }
        .health-badge.degraded { background: var(--warning-light); color: var(--warning); }
        .health-pulse { width: 6px; height: 6px; border-radius: 50%; background: currentColor; animation: pulse 2s infinite; }

        /* Terminal Window Aesthetic */
        .terminal-window {
            background: #141724; border: 1px solid #282d47; border-radius: 8px;
            box-shadow: 0 8px 24px rgba(0,0,0,0.3); overflow: hidden;
        }
        .terminal-header {
            background: #1c2136; padding: 8px 14px; display: flex; align-items: center;
            justify-content: space-between; border-bottom: 1px solid #282d47;
        }
        .terminal-dots { display: flex; gap: 6px; align-items: center; }
        .terminal-dots span { width: 10px; height: 10px; border-radius: 50%; display: inline-block; }
        .terminal-dots span:nth-child(1) { background: #ff5f56; }
        .terminal-dots span:nth-child(2) { background: #ffbd2e; }
        .terminal-dots span:nth-child(3) { background: #27c93f; }
        .terminal-title { font-size: 11px; font-family: monospace; color: #94a3b8; font-weight: 600; }
        .terminal-body { padding: 14px 18px; color: #e2e8f0; }
        .terminal-body pre { color: #e2e8f0 !important; }

        /* JSON Syntax Highlighting Tokens in Terminal */
        .jk { color: #00cfd5; font-weight: 600; }
        .js { color: #28c76f; }
        .jn { color: #ff9f43; font-weight: 500; }
        .jb { color: #7367f0; font-weight: 600; }

        .copy-btn {
            background: rgba(255,255,255,0.08); border: 1px solid rgba(255,255,255,0.15);
            color: #94a3b8; font-size: 11px; padding: 3px 8px; border-radius: 4px;
            cursor: pointer; transition: var(--transition);
        }
        .copy-btn:hover { background: var(--primary); color: #fff; border-color: var(--primary); }

        /* Theme Customizer Drawer (Offcanvas) */
        .theme-drawer-backdrop {
            position: fixed; inset: 0; background: rgba(0,0,0,0.45); z-index: 1200;
            display: none; opacity: 0; transition: opacity 0.25s ease;
        }
        .theme-drawer-backdrop.active { display: block !important; opacity: 1; }

        .theme-drawer {
            position: fixed; top: 0; right: 0; width: 320px; height: 100vh;
            background: var(--bg-primary); border-left: 1px solid var(--border);
            z-index: 1300; box-shadow: -5px 0 25px rgba(0,0,0,0.25);
            transform: translateX(100%);
            transition: transform 0.3s cubic-bezier(0.4, 0, 0.2, 1);
            display: flex; flex-direction: column;
        }
        .theme-drawer.active { transform: translateX(0) !important; }
        .theme-drawer-header {
            padding: 20px 24px; border-bottom: 1px solid var(--border);
            display: flex; align-items: center; justify-content: space-between;
        }
        .theme-drawer-header h3 { font-size: 16px; font-weight: 700; color: var(--text-primary); }
        .theme-drawer-close { background: none; border: none; font-size: 20px; color: var(--text-secondary); cursor: pointer; }
        .theme-drawer-body { padding: 24px; flex: 1; overflow-y: auto; display: flex; flex-direction: column; gap: 20px; }
        .theme-section-title { font-size: 12px; font-weight: 700; color: var(--text-tertiary); text-transform: uppercase; letter-spacing: 0.5px; }

        .theme-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 12px; }
        .theme-card {
            border: 2px solid var(--border); border-radius: 8px; padding: 12px;
            cursor: pointer; transition: var(--transition); text-align: center;
        }
        .theme-card:hover { border-color: var(--primary); transform: translateY(-2px); }
        .theme-card.active { border-color: var(--primary); background: var(--primary-light); }
        .theme-preview-box { height: 40px; border-radius: 6px; margin-bottom: 8px; border: 1px solid rgba(0,0,0,0.1); }
        .theme-label { font-size: 12px; font-weight: 600; color: var(--text-primary); }

        /* Standard Cards & Tables */
        .page-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 24px; }
        .page-title { font-size: 24px; font-weight: 700; color: var(--text-primary); margin-bottom: 4px; }
        .page-subtitle { font-size: 14px; color: var(--text-secondary); }

        .stats-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 24px; margin-bottom: 32px; }
        .stat-card {
            background: var(--bg-primary); border-radius: var(--radius); padding: 20px;
            box-shadow: var(--shadow); transition: var(--transition);
            border: 1px solid var(--border); display: flex; gap: 16px; align-items: center;
        }
        .stat-card:hover { border-color: var(--primary); transform: translateY(-3px); }
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

        .card { background: var(--bg-primary); border-radius: var(--radius); box-shadow: var(--shadow); border: 1px solid var(--border); overflow: hidden; margin-bottom: 24px; }
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

        .bulk-toolbar { position: fixed; bottom: 24px; left: calc(var(--sidebar-width) + 30px); right: 30px; background: var(--bg-primary); border: 1px solid var(--primary); border-radius: var(--radius); padding: 16px 24px; display: none; align-items: center; justify-content: space-between; box-shadow: 0 10px 40px rgba(0,0,0,0.3); z-index: 1000; }
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

        /* Cluster Topology Map */
        .topology-card { margin-bottom: 24px; padding: 20px; background: var(--bg-primary); border-radius: var(--radius); border: 1px solid var(--border); box-shadow: var(--shadow); }
        .topology-header { display: flex; align-items: center; justify-content: space-between; margin-bottom: 16px; }
        .topology-diagram { display: grid; grid-template-columns: 1fr 40px 1fr 40px 1fr; align-items: center; gap: 8px; overflow-x: auto; padding: 10px 0; }
        .topo-col { display: flex; flex-direction: column; gap: 10px; }
        .topo-arrow { display: flex; align-items: center; justify-content: center; color: var(--primary); }
        .topo-arrow svg { animation: pulseArrow 2s infinite ease-in-out; }
        @keyframes pulseArrow { 0%, 100% { transform: translateX(0); opacity: 0.6; } 50% { transform: translateX(4px); opacity: 1; } }
        .topo-box { background: var(--bg-secondary); border: 1px solid var(--border); border-radius: 8px; padding: 12px 14px; transition: var(--transition); }
        .topo-box:hover { border-color: var(--primary); transform: translateY(-2px); box-shadow: 0 4px 12px rgba(0,0,0,0.15); }
        .topo-title { font-size: 11px; font-weight: 700; color: var(--text-tertiary); text-transform: uppercase; margin-bottom: 4px; display: flex; align-items: center; justify-content: space-between; }
        .topo-val { font-size: 13px; font-weight: 600; color: var(--text-primary); }
        .topo-sub { font-size: 11px; color: var(--text-secondary); margin-top: 2px; }
        .pulse-live { width: 6px; height: 6px; border-radius: 50%; background: var(--success); display: inline-block; box-shadow: 0 0 8px var(--success); }
        """;

    private static readonly string CoreVersion = GetAssemblyVersion(typeof(JobRecord).Assembly);
    private static readonly string DashboardVersion = GetAssemblyVersion(typeof(HtmlShell).Assembly);

    /// <summary>Wraps the content in the standard HTML shell.</summary>
    internal static string Wrap(
        string title,
        string pathPrefix,
        string activeRoute,
        string body,
        NavCounters? counters = null,
        JobMetrics? metrics = null,
        IReadOnlyList<DashboardCluster>? clusters = null,
        DashboardCluster? activeCluster = null) =>
        $$"""
        <!DOCTYPE html>
        <html lang="en" data-theme="blue-theme">
        <head>
            <meta charset="utf-8" /><meta name="viewport" content="width=device-width, initial-scale=1" /><title>{{title}}</title>
            <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Public+Sans:wght@400;500;600;700&display=swap">
            <style>{{Css}}</style>
        </head>
        <body>
        <!-- Top Header (Maxton 64px) -->
        <header class="top-header">
            <div class="header-left">
                <a href="{{pathPrefix}}" class="header-logo">
                    <h1>NexJob</h1>
                    <span class="logo-badge">Pro</span>
                </a>
                <button type="button" class="btn-toggle-sidebar" onclick="nexJobToggleSidebar()" title="Toggle Sidebar">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="3" y1="12" x2="21" y2="12"/><line x1="3" y1="6" x2="21" y2="6"/><line x1="3" y1="18" x2="21" y2="18"/></svg>
                </button>
                <div class="header-search" onclick="document.getElementById('header-search-input').focus()">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="color:var(--text-tertiary)"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
                    <input type="text" id="header-search-input" placeholder="Search jobs, queues, tags..." onkeydown="if(event.key==='Enter'){window.location.href='{{pathPrefix}}/jobs?q='+encodeURIComponent(this.value);}" />
                    <kbd>Ctrl + K</kbd>
                </div>
            </div>
            <div class="header-right">
                {{ClusterSwitcher(clusters, activeCluster)}}
                {{HealthBadge(metrics)}}
                <button type="button" class="header-btn theme-customizer-btn" onclick="nexJobToggleDrawer(true)" title="Theme Customizer">
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="13.5" cy="6.5" r=".5" fill="currentColor"/><circle cx="17.5" cy="10.5" r=".5" fill="currentColor"/><circle cx="8.5" cy="7.5" r=".5" fill="currentColor"/><circle cx="6.5" cy="12.5" r=".5" fill="currentColor"/><path d="M12 2C6.5 2 2 6.5 2 12s4.5 10 10 10c.926 0 1.648-.746 1.648-1.688 0-.437-.18-.835-.437-1.125-.29-.289-.438-.652-.438-1.125a1.64 1.64 0 0 1 1.668-1.668h1.996c3.051 0 5.563-2.512 5.563-5.563C22 6.5 17.5 2 12 2z"/></svg>
                    <span>Themes</span>
                </button>
                <a href="https://github.com/oluciano/NexJob" target="_blank" class="header-btn" title="GitHub Repository">
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 19c-5 1.5-5-2.5-7-3m14 6v-3.87a3.37 3.37 0 0 0-.94-2.61c3.14-.35 6.44-1.54 6.44-7A5.44 5.44 0 0 0 20 4.77 5.07 5.07 0 0 0 19.91 1S18.73.65 16 2.48a13.38 13.38 0 0 0-7 0C6.27.65 5.09 1 5.09 1A5.07 5.07 0 0 0 5 4.77a5.44 5.44 0 0 0-1.5 3.78c0 5.42 3.3 6.61 6.44 7A3.37 3.37 0 0 0 9 18.13V22"/></svg>
                    <span>Docs ↗</span>
                </a>
            </div>
        </header>

        <!-- Main Wrapper -->
        <div class="app-container">
            <!-- Categorized Sidebar Navigation -->
            <nav class="sidebar" id="sidebar">
                <div class="nav-scroller">
                    <div class="nav-category-title">MONITORING</div>
                    <a href="{{pathPrefix}}" class="nav-item {{Active(activeRoute, "overview")}}" title="Overview">
                        <div class="nav-item-left">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/></svg>
                            <span class="nav-label">Overview</span>
                        </div>
                    </a>
                    <a href="{{pathPrefix}}/queues" class="nav-item {{Active(activeRoute, "queues")}}" title="Queues">
                        <div class="nav-item-left">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><line x1="8" y1="6" x2="21" y2="6"/><line x1="8" y1="12" x2="21" y2="12"/><line x1="8" y1="18" x2="21" y2="18"/><line x1="3" y1="6" x2="3.01" y2="6"/><line x1="3" y1="12" x2="3.01" y2="12"/><line x1="3" y1="18" x2="3.01" y2="18"/></svg>
                            <span class="nav-label">Queues</span>
                        </div>
                        {{NavCounter(counters?.Queues, counters?.QueuesClass)}}
                    </a>
                    <a href="{{pathPrefix}}/servers" class="nav-item {{Active(activeRoute, "servers")}}" title="Servers">
                        <div class="nav-item-left">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="2" width="20" height="8" rx="2" ry="2"/><rect x="2" y="14" width="20" height="8" rx="2" ry="2"/><line x1="6" y1="6" x2="6.01" y2="6"/><line x1="6" y1="18" x2="6.01" y2="18"/></svg>
                            <span class="nav-label">Servers</span>
                        </div>
                        {{NavCounter(counters?.Servers, counters?.ServersClass)}}
                    </a>
                    <a href="{{pathPrefix}}/listeners" class="nav-item {{Active(activeRoute, "listeners")}}" title="Listeners">
                        <div class="nav-item-left">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M4.9 19.1C1 15.2 1 8.8 4.9 4.9"/><path d="M7.8 16.2c-2.3-2.3-2.3-6.1 0-8.5"/><circle cx="12" cy="12" r="2"/><path d="M16.2 7.8c2.3 2.3 2.3 6.1 0 8.5"/><path d="M19.1 4.9C23 8.8 23 15.1 19.1 19.1"/></svg>
                            <span class="nav-label">Listeners</span>
                        </div>
                        {{NavCounter(counters?.Listeners, counters?.ListenersClass)}}
                    </a>

                    <div class="nav-category-title">EXECUTION</div>
                    <a href="{{pathPrefix}}/jobs" class="nav-item {{Active(activeRoute, "jobs")}}" title="Jobs">
                        <div class="nav-item-left">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="12 2 2 7 12 12 22 7 12 2"/><polyline points="2 17 12 22 22 17"/><polyline points="2 12 12 17 22 12"/></svg>
                            <span class="nav-label">Jobs</span>
                        </div>
                        {{NavCounter(counters?.Jobs, null)}}
                    </a>
                    <a href="{{pathPrefix}}/recurring" class="nav-item {{Active(activeRoute, "recurring")}}" title="Recurring">
                        <div class="nav-item-left">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
                            <span class="nav-label">Recurring</span>
                        </div>
                        {{NavCounter(counters?.Recurring, null)}}
                    </a>
                    <a href="{{pathPrefix}}/failed" class="nav-item {{Active(activeRoute, "failed")}}" title="Failed / DLQ">
                        <div class="nav-item-left">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/></svg>
                            <span class="nav-label">Failed / DLQ</span>
                        </div>
                        {{NavCounter(counters?.Failed, counters?.FailedClass)}}
                    </a>

                    <div class="nav-category-title">SYSTEM</div>
                    <a href="{{pathPrefix}}/settings" class="nav-item {{Active(activeRoute, "settings")}}" title="Settings">
                        <div class="nav-item-left">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>
                            <span class="nav-label">Settings</span>
                        </div>
                    </a>
                </div>
                <div class="sidebar-footer">
                    <span>NexJob Core v{{CoreVersion}}</span>
                    <a href="https://github.com/oluciano/NexJob/releases/tag/v{{DashboardVersion}}" target="_blank" style="color:var(--text-secondary);text-decoration:none;font-weight:600">v{{DashboardVersion}}</a>
                </div>
            </nav>

            <!-- Main Page Content -->
            <main class="main-content">{{body}}</main>
        </div>

        <!-- Bulk Actions Floating Bar -->
        <div id="bulk-toolbar" class="bulk-toolbar">
            <div class="bulk-info"><span id="bulk-count">0</span> jobs selected</div>
            <div class="bulk-actions">
                <button id="bulk-requeue-btn" class="btn btn-primary" onclick="nexJobBulkAction('requeue')">↺ Requeue</button>
                <button id="bulk-delete-btn" class="btn btn-danger" onclick="nexJobBulkAction('delete')">Delete</button>
                <button class="btn btn-secondary" onclick="nexJobClearSelection()">Cancel</button>
            </div>
        </div>

        <!-- Theme Customizer Offcanvas Drawer -->
        <div id="theme-drawer-backdrop" class="theme-drawer-backdrop" onclick="nexJobToggleDrawer(false)"></div>
        <aside id="theme-drawer" class="theme-drawer">
            <div class="theme-drawer-header">
                <h3>Theme Customizer</h3>
                <button type="button" class="theme-drawer-close" onclick="nexJobToggleDrawer(false)">&times;</button>
            </div>
            <div class="theme-drawer-body">
                <div class="theme-section-title">Color Themes</div>
                <div class="theme-grid">
                    <div class="theme-card" data-theme="dark" onclick="nexJobSetTheme('dark')">
                        <div class="theme-preview-box" style="background:#25293c;border:2px solid #43495e"></div>
                        <div class="theme-label">Dark</div>
                    </div>
                    <div class="theme-card" data-theme="light" onclick="nexJobSetTheme('light')">
                        <div class="theme-preview-box" style="background:#f8f7fa;border:2px solid #dbdade"></div>
                        <div class="theme-label">Light</div>
                    </div>
                    <div class="theme-card" data-theme="blue-theme" onclick="nexJobSetTheme('blue-theme')">
                        <div class="theme-preview-box" style="background:#0f1535;border:2px solid #00cfd5"></div>
                        <div class="theme-label">Blue Theme</div>
                    </div>
                    <div class="theme-card" data-theme="semi-dark" onclick="nexJobSetTheme('semi-dark')">
                        <div class="theme-preview-box" style="background:linear-gradient(90deg,#131623 35%,#ffffff 35%);border:2px solid #e2e8f0"></div>
                        <div class="theme-label">Semi-Dark</div>
                    </div>
                    <div class="theme-card" data-theme="bordered-theme" onclick="nexJobSetTheme('bordered-theme')">
                        <div class="theme-preview-box" style="background:#ffffff;border:2px solid #c8c7ce"></div>
                        <div class="theme-label">Bordered</div>
                    </div>
                </div>
            </div>
        </aside>

        <script>
        (function(){
            var h = document.documentElement;

            window.nexJobSetTheme = function(theme, save) {
                if(save!==false) localStorage.setItem('nexjob-theme', theme);
                h.setAttribute('data-theme', theme);
                document.querySelectorAll('.theme-card').forEach(function(el){
                    if(el.getAttribute('data-theme')===theme){ el.classList.add('active'); }
                    else { el.classList.remove('active'); }
                });
            };

            window.nexJobToggleSidebar = function() {
                var s=document.getElementById('sidebar');
                if(!s) return;
                s.classList.toggle('collapsed');
                localStorage.setItem('nexjob-sidebar', s.classList.contains('collapsed') ? 'collapsed' : 'expanded');
            };

            window.nexJobToggleDrawer = function(open) {
                var d=document.getElementById('theme-drawer');
                var b=document.getElementById('theme-drawer-backdrop');
                if(!d||!b) return;
                if(open){ d.classList.add('active'); b.classList.add('active'); }
                else { d.classList.remove('active'); b.classList.remove('active'); }
            };

            var storedTheme=localStorage.getItem('nexjob-theme')||'blue-theme';
            nexJobSetTheme(storedTheme, false);

            var storedSidebar=localStorage.getItem('nexjob-sidebar');
            if(storedSidebar==='collapsed'){
                var s=document.getElementById('sidebar');
                if(s) s.classList.add('collapsed');
            }

            window.addEventListener('keydown', function(e){
                if((e.ctrlKey||e.metaKey) && e.key==='k'){
                    e.preventDefault();
                    var inp=document.getElementById('header-search-input');
                    if(inp){ inp.focus(); inp.select(); }
                }
            });

            window.nexJobUpdateSelection = function() {
                var checked = document.querySelectorAll('.job-check:checked');
                var bar = document.getElementById('bulk-toolbar');
                var count = document.getElementById('bulk-count');
                if (checked.length > 0) { count.textContent = checked.length; bar.style.display = 'flex'; }
                else { bar.style.display = 'none'; }
            };

            window.nexJobClearSelection = function() { document.querySelectorAll('.job-check:checked').forEach(c => c.checked = false); nexJobUpdateSelection(); };
            
            window.nexJobSwitchCluster = function(select) {
                var clusterId = select.value;
                var url = new URL(window.location.href);
                if (clusterId) {
                    url.searchParams.set('cluster', clusterId);
                } else {
                    url.searchParams.delete('cluster');
                }
                window.location.href = url.toString();
            };

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

    private static string GetAssemblyVersion(Assembly assembly)
    {
        var infoVer = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(infoVer))
        {
            var plusIdx = infoVer.IndexOf('+', StringComparison.Ordinal);
            return plusIdx > 0 ? infoVer[..plusIdx] : infoVer;
        }

        return assembly.GetName().Version?.ToString(3) ?? "5.3.0";
    }

    private static string ClusterSwitcher(IReadOnlyList<DashboardCluster>? clusters, DashboardCluster? activeCluster)
    {
        if (clusters is null || clusters.Count <= 1)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("<div class=\"cluster-switcher\" style=\"display:flex;align-items:center;gap:6px;margin-right:12px\">");
        sb.Append("<svg width=\"15\" height=\"15\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" style=\"color:var(--text-tertiary)\"><ellipse cx=\"12\" cy=\"5\" rx=\"9\" ry=\"3\"></ellipse><path d=\"M21 12c0 1.66-4 3-9 3s-9-1.34-9-3\"></path><path d=\"M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5\"></path></svg>");
        sb.Append("<select onchange=\"nexJobSwitchCluster(this)\" title=\"Switch Cluster\" style=\"font-size:12px;padding:4px 8px;font-weight:600;border-radius:6px;background:var(--bg-secondary);border:1px solid var(--border);color:var(--text-primary);cursor:pointer\">");

        foreach (var c in clusters)
        {
            var isSelected = activeCluster is not null && string.Equals(c.Id, activeCluster.Id, StringComparison.OrdinalIgnoreCase);
            var selectedAttr = isSelected ? " selected" : string.Empty;
            sb.Append($"<option value=\"{System.Web.HttpUtility.HtmlAttributeEncode(c.Id)}\"{selectedAttr}>Cluster: {System.Web.HttpUtility.HtmlEncode(c.Name)}</option>");
        }

        sb.Append("</select></div>");
        return sb.ToString();
    }
}
