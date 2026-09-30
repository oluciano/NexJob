# NexJob.Dashboard

Embedded real-time monitoring dashboard middleware for **NexJob** in ASP.NET Core applications.

Provides a lightweight, zero-dependency web interface for monitoring background jobs, active worker nodes, queue throughput, and recurring cron schedules with live updates.

[![NexJob Dashboard Overview](https://raw.githubusercontent.com/oluciano/NexJob/develop/docs/assets/dashboard-overview.png)](https://raw.githubusercontent.com/oluciano/NexJob/develop/docs/assets/dashboard-overview.png)

---

## Installation

```bash
dotnet add package NexJob.Dashboard
```

---

## Quick Start

Enable the dashboard middleware in `Program.cs`:

```csharp
using NexJob;
using NexJob.Dashboard;

var builder = WebApplication.CreateBuilder(args);

// Register MemoryCache (required for metrics caching)
builder.Services.AddMemoryCache();

// Register NexJob
builder.Services.AddNexJobPostgres(builder.Configuration.GetConnectionString("NexJob")!);
builder.Services.AddNexJob();

var app = builder.Build();

// Mount dashboard middleware at /dashboard
app.UseNexJobDashboard("/dashboard", options =>
{
    options.Title = "NexJob Management Console";
    options.MetricsCacheTtl = TimeSpan.FromSeconds(3);
});

app.Run();
```

---

## Features

- **Multi-Cluster Dashboard Federation:** Seamlessly aggregate multiple isolated NexJob clusters (regional, multi-tenant, or staging vs production) in a single dashboard with live cluster switching dropdown (`?cluster={id}`), cluster-isolated SSE caching, and read-only cluster protection.
- **Enterprise Maxton Design System:** Complete visual overhaul inspired by the modern Maxton UI layout, featuring an executive 64px Top Header with responsive sidebar toggle (☰), global keyboard search (`Ctrl + K`), live cluster health status badge (`HEALTHY`, `DEGRADED`, `INCIDENT`), and docs shortcuts.
- **Multi-Theme Switcher (5 Themes):** Instant 1-click theme customizer offcanvas drawer supporting `Blue Theme` (Midnight - default), `Dark`, `Light`, `Semi-Dark` (dark sidebar/header with light content), and `Bordered` (clean 1px high-contrast borders without heavy shadows), fully persisted in `localStorage`.
- **Cluster Pipeline Topology Map:** Native SVG & animated CSS flowchart connecting Ingress & Triggers ➔ Queue Buffers ➔ Processing Workers with live activity pulse indicators.
- **Real-Time Live Log Streaming (SSE):** Streaming log viewer on `/jobs/{id}` dynamically appending execution console logs in real-time.
- **Event Triggers & Listeners Visibility:** Dedicated `/listeners` page rendering active broker connections (RabbitMQ, Kafka, Azure Service Bus, SQS, etc.), consumer groups, target queues/topics, uptime, and real-time status transitions (`Starting`, `Listening`, `Reconnecting`, `Faulted`, `Stopped`).
- **Time-Window Filtering & Queue Controls:** Quick period filters (`1h`, `6h`, `24h`, `7d`, `All Time`) on `/jobs` and interactive `Pause` / `Resume` buttons on `/queues`.
- **Job Catalog & Definitions (`/catalog`):** Aggregated job definitions table reporting run volume, error rates, average duration, last execution timestamps, deep links to `/jobs`, and ad-hoc trigger execution for parameterless jobs.
- **Modernized Terminal & JSON Viewers:** Execution logs and payload viewers formatted into dark terminal-styled code windows with header dots, syntax color tokens, and 1-click clipboard copy buttons.
- **Live Overview:** Real-time counters for `Enqueued`, `Processing`, `Succeeded`, `Failed`, `Expired`, `Retrying`, and `Dead-Letter` jobs via Server-Sent Events (SSE).
- **Job Details & Inspection:** View serialized input arguments, execution history, exception stack traces, and captured console logs.
- **Recurring Jobs:** Monitor cron schedules, last execution timestamp, next scheduled run, and manually trigger recurring jobs on demand.
- **Worker Nodes & Heartbeats:** Live view of registered worker instances, process IDs, and health status.
- **Read Replica Isolation:** Compatible with `UseDashboardReadReplica()` to ensure monitoring traffic does not impact primary database write performance.
- **Zero External Dependencies:** 100% self-contained in native CSS and vanilla JS — zero external NPM, Webpack, or CDN dependencies.

---

## Multi-Cluster Federation

Aggregate multiple clusters in a single dashboard instance:

```csharp
app.UseNexJobDashboard("/dashboard", options =>
{
    options.Title = "Global Ops Hub";
    options.AddCluster(new DashboardCluster("us-east", "US-East Production", usEastStorage, isReadOnly: true));
    options.AddCluster(new DashboardCluster("eu-west", "EU-West Production", euWestStorage, isReadOnly: true));
    options.AddCluster(new DashboardCluster("staging", "Staging", stagingStorage, isReadOnly: false));
});
```

---

## Securing the Dashboard

By default, the dashboard is open to all incoming requests. To protect it in staging and production, implement `IDashboardAuthorizationHandler`:

```csharp
using NexJob;

public class AdminDashboardAuthorization : IDashboardAuthorizationHandler
{
    public Task<bool> AuthorizeAsync(HttpContext context)
    {
        // Example: Only allow authenticated users with the "Admin" or "Ops" role
        var isAuthorized = context.User.Identity?.IsAuthenticated == true &&
                           (context.User.IsInRole("Admin") || context.User.IsInRole("Ops"));

        return Task.FromResult(isAuthorized);
    }
}
```

Register the authorization handler in DI:

```csharp
builder.Services.AddSingleton<IDashboardAuthorizationHandler, AdminDashboardAuthorization>();
```

When authorization fails, the dashboard returns `401 Unauthorized` or `403 Forbidden`.

---

## Options

| Option | Type | Default | Description |
|---|---|---|---|
| `Title` | `string` | `"NexJob"` | Title shown in the browser tab and sidebar header |
| `MetricsCacheTtl` | `TimeSpan` | `3s` | Cache duration for dashboard metrics to prevent DB overload during SSE polling |
| `Queues` | `IReadOnlyList<string>?` | `null` | Optional list of queues to scope the dashboard view, nav counters, and default job listings |
| `Clusters` | `IReadOnlyList<DashboardCluster>` | `[]` | Registered federated clusters for multi-cluster operations |
