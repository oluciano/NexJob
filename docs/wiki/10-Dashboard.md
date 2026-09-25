# Dashboard

Monitor, debug, and manage jobs through the enterprise Maxton-inspired UI.

[![NexJob Enterprise Dashboard Overview](../assets/dashboard-overview.png)](../assets/dashboard-overview.png)

---

## Features

- **Maxton Design System:** Complete visual modernization with a 64px Top Header, responsive collapsible sidebar toggle (☰), live cluster health indicator (`HEALTHY`, `DEGRADED`, `INCIDENT`), and keyboard shortcut search (`Ctrl + K`).
- **5 Built-In Themes:** Instant 1-click theme customizer offcanvas drawer supporting `Blue Theme` (Midnight - default), `Dark`, `Light`, `Semi-Dark`, and `Bordered`, fully persisted in `localStorage`.
- **Cluster Pipeline Topology Map:** Native animated SVG & CSS flowchart connecting Ingress & Triggers ➔ Queue Buffers ➔ Processing Workers with live activity pulses.
- **Real-Time Live Log Streaming (SSE):** Streaming log viewer on `/jobs/{id}` displaying logs line-by-line via Server-Sent Events as the job executes.
- **Active Event Triggers & Listeners:** Dedicated `/listeners` page monitoring connected message brokers (RabbitMQ, Kafka, SQS, Azure Service Bus, etc.), consumer groups, target queues, and status (`Listening`, `Reconnecting`, `Faulted`).
- **Interactive Controls & Time Filters:** Pause and Resume queues with 1 click; filter jobs by time periods (`1h`, `6h`, `24h`, `7d`).
- **Zero External Dependencies:** 100% self-contained in native CSS and vanilla JS — no external NPM, Webpack, or CDN downloads required.

---

## Packages

The dashboard UI is self-contained and pre-packaged, but kept in dedicated NuGet packages to avoid dragging web dependencies into headless workers:

| Application Type | NuGet Package | Setup Method |
|---|---|---|
| **ASP.NET Core Web App** | `NexJob.Dashboard` | `app.UseNexJobDashboard()` |
| **Worker Service / Console** | `NexJob.Dashboard.Standalone` | `services.AddNexJobStandaloneDashboard()` |

---

## ASP.NET Core

### 1. Install Package

```bash
dotnet add package NexJob.Dashboard
```

### 2. Configure `Program.cs`

> [!IMPORTANT]
> `builder.Services.AddMemoryCache()` is registered automatically by `AddNexJob()`. You can configure dashboard options via delegate or `appsettings.json`.

```csharp
using NexJob;
using NexJob.Dashboard;

var builder = WebApplication.CreateBuilder(args);

// Register NexJob (registers in-memory storage by default, or your preferred provider)
builder.Services.AddNexJob();

var app = builder.Build();

// Mount dashboard middleware (default: /dashboard)
app.UseNexJobDashboard();

// Or custom path and options:
// app.UseNexJobDashboard("/dashboard", options =>
// {
//     options.Title = "Enterprise NexJob Console";
//     options.MetricsCacheTtl = TimeSpan.FromSeconds(3);
// });

app.Run();
```

---

## Standalone (Worker Services)

For Worker Services or console applications that do not have their own ASP.NET Core HTTP pipeline, use the standalone dashboard. It runs an embedded HTTP server hosting the UI.

### 1. Install Package

```bash
dotnet add package NexJob.Dashboard.Standalone
```

### 2. Configure `Program.cs`

```csharp
using NexJob;
using NexJob.Dashboard.Standalone;

var builder = Host.CreateApplicationBuilder(args);

// Register NexJob
builder.Services.AddNexJob();

// Registers embedded HTTP server (default: http://localhost:5005/dashboard)
builder.Services.AddNexJobStandaloneDashboard();

// Or customize host and port:
// builder.Services.AddNexJobStandaloneDashboard(options =>
// {
//     options.Port = 5005;
//     options.Path = "/dashboard";
//     options.Title = "Worker Dashboard";
//     options.LocalhostOnly = true;
// });

var host = builder.Build();
host.Run();
```

---

## Authorization

By default, the dashboard is open. Add authorization by implementing `IDashboardAuthorizationHandler`.

```csharp
public sealed class AdminDashboardAuth : IDashboardAuthorizationHandler
{
    public Task<bool> AuthorizeAsync(HttpContext context)
    {
        // Example: check for admin role
        return Task.FromResult(context.User.IsInRole("Admin"));
    }
}

builder.Services.AddTransient<IDashboardAuthorizationHandler, AdminDashboardAuth>();
```

Multiple handlers are allowed — any handler returning `true` grants access.

---

## Debugging Failed Jobs

1. Open the dashboard and navigate to **Failed** tab
2. Click on a failed job to see:
   - Error message and full stack trace
   - Number of attempts and retry timeline
   - Job input (deserialized)
   - Queue, tags, and creation/completion timestamps
3. Use this information to diagnose and fix the issue

---

## Reading the Execution Timeline

Each job displays its lifecycle:

```
Created:  2026-04-08 10:00:00 UTC
Enqueued: 2026-04-08 10:00:01 UTC
Started:  2026-04-08 10:00:03 UTC  (2s queue wait)
Failed:   2026-04-08 10:00:05 UTC  (2s execution)
Retried:  2026-04-08 10:00:35 UTC  (30s backoff)
Started:  2026-04-08 10:00:36 UTC
Succeeded:2026-04-08 10:00:38 UTC
```

Key timestamps to check:

- **Queue wait time** = `Started - Enqueued` — high values indicate insufficient workers
- **Execution time** = `Completed - Started` — high values indicate slow job or external dependency
- **Retry gaps** — show backoff delays between attempts

---

## Requeuing Safely

The dashboard allows requeuing failed or expired jobs:

1. Select the job in the **Failed** or **Expired** tab
2. Click **Requeue**
3. A new `JobRecord` is created with the same input, preserving the original for audit

**Important:** Requeue creates a new job — it does not modify the existing one. The original job remains in its terminal state for historical tracking.

---

## Monitoring Queues

The dashboard shows:

- Queue depth (jobs waiting per queue)
- Processing jobs (currently executing)
- Paused queues (managed via runtime settings)
- Worker count and active servers

---

## Next Steps

- [Configuration Reference](11-Configuration-Reference.md) — Dashboard options
- [Troubleshooting](16-Troubleshooting.md) — Dashboard not showing jobs
- [Best Practices](13-Best-Practices.md) — Production dashboard guidelines
