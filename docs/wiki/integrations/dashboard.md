---
title: "NexJob Dashboard: Monitor and Manage Background Jobs"
sidebarTitle: "Dashboard"
description: "Set up the NexJob built-in dashboard for ASP.NET Core or Worker Services to monitor job queues, cluster topology, live logs, and event triggers."
---

The NexJob Dashboard gives you a real-time window into every aspect of your background job infrastructure — from individual job execution timelines and live SSE log streams to cluster-wide topology maps and connected broker listeners. Built on the Maxton design system, it runs entirely from a self-contained NuGet package with no JavaScript framework, no build toolchain, and no CDN dependencies (except the Public Sans web font from Google Fonts).

## Features

<div class="grid cards" markdown>
  -   **Maxton Design System**

    64px top header with a responsive collapsible sidebar, a live cluster health indicator (`HEALTHY`, `DEGRADED`, `INCIDENT`), and `Ctrl+K` keyboard-shortcut search.

  -   **5 Built-In Themes**

    One-click theme customizer offcanvas drawer: **Blue Theme** (Midnight — default), **Dark**, **Light**, **Semi-Dark**, and **Bordered**. Selection is persisted in `localStorage`.

  -   **Cluster Pipeline Topology Map**

    Native animated SVG/CSS flowchart connecting Ingress & Triggers → Queue Buffers → Processing Workers with live activity pulses.

  -   **Real-Time SSE Log Streaming**

    Streaming log viewer on `/jobs/{id}` that displays log lines one-by-one via Server-Sent Events as the job executes.

  -   **Live Event Listeners**

    Dedicated `/listeners` page monitoring connected message brokers (RabbitMQ, Kafka, SQS, Azure Service Bus, and more), consumer groups, target queues, and live status (`Listening`, `Reconnecting`, `Faulted`).

  -   **Job Catalog**

    Aggregated telemetry per job type: total executions, success/failure counts, failure rate percentage, average duration, and on-demand ad-hoc execution for parameterless jobs.

  -   **Pause & Resume Queues**

    Pause and resume any queue with a single click. Filter jobs by time period (`1h`, `6h`, `24h`, `7d`). Bulk requeue failed or expired jobs.

  -   **Ctrl+K Search**

    Keyboard-shortcut search across job types, queues, and IDs — accessible from any page in the dashboard.

</div>

---

## Installation

Choose the package that matches your application type:

| Application Type | NuGet Package | Setup Method |
|---|---|---|
| **ASP.NET Core Web App** | `NexJob.Dashboard` | `app.UseNexJobDashboard()` |
| **Worker Service / Console** | `NexJob.Dashboard.Standalone` | `services.AddNexJobStandaloneDashboard()` |


  === "ASP.NET Core"
    #### Install the package

    ```bash
            dotnet add package NexJob.Dashboard
            ```

          #### Register the middleware in Program.cs

    `AddNexJob()` automatically registers `IMemoryCache` — no extra call required. Mount the dashboard middleware after building the app:

            ```csharp
            using NexJob;
            using NexJob.Dashboard;

            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddNexJob();

            var app = builder.Build();

            // Mount at the default path: /dashboard
            app.UseNexJobDashboard();

            // Or supply a custom path and options:
            // app.UseNexJobDashboard("/dashboard", options =>
            // {
            //     options.Title = "Enterprise NexJob Console";
            //     options.MetricsCacheTtl = TimeSpan.FromSeconds(3);
            //     options.Queues = ["billing", "invoices"]; // scope to specific queues
            // });

            app.Run();
            ```

  === "Worker Service / Standalone"
    #### Install the package

    ```bash
            dotnet add package NexJob.Dashboard.Standalone
            ```

          #### Register the embedded HTTP server in Program.cs

    The standalone dashboard runs its own embedded HTTP server, so no ASP.NET Core pipeline is required:

            ```csharp
            using NexJob;
            using NexJob.Dashboard.Standalone;

            var builder = Host.CreateApplicationBuilder(args);

            builder.Services.AddNexJob();

            // Default: http://localhost:5005/dashboard
            builder.Services.AddNexJobStandaloneDashboard();

            // Or customize host and port:
            // builder.Services.AddNexJobStandaloneDashboard(options =>
            // {
            //     options.Port = 5005;
            //     options.Path = "/dashboard";
            //     options.Title = "Worker Dashboard";
            //     options.LocalhostOnly = true;       // default: loopback only
            //     options.PollIntervalSeconds = 3;    // default: 3
            // });

            var host = builder.Build();
            host.Run();
            ```

            You can also bind from `appsettings.json` using the `NexJob:Dashboard` section:

            ```json
            {
              "NexJob": {
                "Dashboard": {
                  "Port": 5005,
                  "Path": "/dashboard",
                  "Title": "My Worker Jobs"
                }
              }
            }
            ```

            ```csharp
            builder.Services.AddNexJobStandaloneDashboard(builder.Configuration);
            ```



        !!! note
        The standalone dashboard listens on **loopback only** by default (`LocalhostOnly = true`). Inside a container this makes the dashboard unreachable through a published port. Set `LocalhostOnly = false` to listen on all interfaces — but also register an `IDashboardAuthorizationHandler` (see below) or restrict the port with a firewall or network policy. Anyone who can reach an exposed port can read job payloads and invoke actions such as pausing queues or requeuing jobs. NexJob logs a startup warning when `LocalhostOnly = false` and no authorization handler is registered.



---

## Configuration Options

### `DashboardOptions` (ASP.NET Core)

| Option | Type | Default | Description |
|---|---|---|---|
| `Title` | `string` | `"NexJob"` | Browser tab title and header label displayed in the UI. |
| `MetricsCacheTtl` | `TimeSpan` | `TimeSpan.FromSeconds(3)` | How long the dashboard caches aggregated metric results before re-querying storage. Set to `TimeSpan.Zero` to disable caching. |
| `Queues` | `IReadOnlyList<string>?` | `null` (all queues) | Scope the dashboard to a subset of queues. Navigation counters, queue cards, and default job queries are filtered to these queues only. |

### `StandaloneDashboardOptions` (Worker Service)

| Option | Type | Default | Description |
|---|---|---|---|
| `Port` | `int` | `5005` | TCP port the embedded HTTP server binds to. |
| `Path` | `string` | `"/dashboard"` | URL path prefix for all dashboard routes. |
| `Title` | `string` | `"NexJob"` | Browser tab title and header label. |
| `LocalhostOnly` | `bool` | `true` | When `true`, the server binds to `127.0.0.1` only. Set `false` to listen on all interfaces. |
| `PollIntervalSeconds` | `int` | `3` | How often the dashboard front-end polls for updated metrics. |
| `DisableWorkers` | `bool` | `false` | When `true`, sets `Workers = 0` on this host — useful for a dedicated ops dashboard that should not process jobs. |

---

## Dashboard Authorization

By default the dashboard is open to any request. Implement `IDashboardAuthorizationHandler` to add access control:

```csharp
public sealed class AdminDashboardAuth : IDashboardAuthorizationHandler
{
    public Task<bool> AuthorizeAsync(HttpContext context)
    {
        // Allow only authenticated users in the Admin role
        return Task.FromResult(context.User.IsInRole("Admin"));
    }
}

builder.Services.AddTransient<IDashboardAuthorizationHandler, AdminDashboardAuth>();
```

The dashboard uses the **last** `IDashboardAuthorizationHandler` registered in DI. Combine multiple rules inside a single handler. A rejected request receives `401 Unauthorized`.

### Authorization in the standalone dashboard

The standalone dashboard runs no authentication middleware, so `context.User` is always unauthenticated. Your handler must authenticate the request itself from `context.Request`. The following example uses HTTP Basic:

```csharp
public sealed class BasicAuthDashboardHandler(IConfiguration config) : IDashboardAuthorizationHandler
{
    public Task<bool> AuthorizeAsync(HttpContext context)
    {
        var expected = config["Dashboard:Credentials"]; // "user:password"
        if (!string.IsNullOrEmpty(expected)
            && TryReadCredentials(context, out var provided)
            && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(provided)))
        {
            return Task.FromResult(true);
        }

        // Prompt the browser for credentials; the dashboard returns 401.
        context.Response.Headers.WWWAuthenticate = "Basic realm=\"NexJob\"";
        return Task.FromResult(false);
    }

    private static bool TryReadCredentials(HttpContext context, out string credentials)
    {
        credentials = string.Empty;
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            credentials = Encoding.UTF8.GetString(
                Convert.FromBase64String(header["Basic ".Length..].Trim()));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

builder.Services.AddSingleton<IDashboardAuthorizationHandler, BasicAuthDashboardHandler>();
```

!!! warning
    Always put a TLS-terminating proxy in front when using HTTP Basic, because credentials are sent in plaintext. A handler that throws an exception never grants access.


---

## Dashboard Pages

<div class="grid cards" markdown>
  -   **Overview**

    The dashboard home page: cluster health, the cluster pipeline topology map, the 24h hourly throughput chart, and CPU and RAM gauges for the host process.

  -   **/servers**

    Every active node with its state, worker capacity, CPU, memory, polled queues, uptime, and last heartbeat. Node IDs (`{Host}:{PID}:{Guid}`) display as `{Host}:{PID} #{shortGuid}`; hover to see the full ID.

  -   **/jobs**

    Browse all jobs with filters by status, queue, and time period. Click any job to see its full execution timeline, retry history, input payload, and live SSE log stream.

  -   **/queues**

    Per-queue depth, active worker count, and pause/resume controls. Queues with enqueued jobs but no active workers show a `⚠️ NO WORKERS` warning badge. Queues with a [circuit breaker](../guides/circuit-breaker.md) show the current circuit state and a **Reset Circuit** button.

  -   **/failed**

    The dead-letter view. Switch between **Failed** jobs (retries exhausted) and **Expired** jobs (deadline exceeded), then **Requeue All** or **Delete All**. Requeue warns you when target queues have no active workers.

  -   **/recurring**

    Every registered recurring schedule with its status (active, paused, or deleted), cron schedule, queue, last run, and next run. Open a schedule to see its execution history.

  -   **/listeners**

    Live status of every registered broker trigger: endpoint, broker type, consumer group, target queue, and uptime. Status values: `Starting`, `Listening`, `Reconnecting`, `Faulted`, `Stopped`.

  -   **/catalog**

    Distinct job types with aggregated telemetry (executions, success/failure counts, failure rate, average duration). Trigger parameterless jobs on demand and jump to filtered job history.

  -   **/settings**

    View and override runtime settings (polling interval, retention periods) without redeployment. Overrides survive restarts and are shared across all nodes. Read-only clusters reject mutating actions.

</div>

---

## Multi-Cluster Dashboard Federation

Aggregate multiple independent NexJob clusters — production, staging, regional, or multi-tenant — into a single dashboard UI without installing extra packages.

### Registering clusters

```csharp
// ASP.NET Core
app.UseNexJobDashboard("/dashboard", options =>
{
    options.Title = "Global Management Console";

    options.AddCluster(new DashboardCluster(
        id: "prod-us",
        name: "Production (US-East)",
        dashboardStorage: prodStorage,
        isReadOnly: true));   // disables mutating POST actions

    options.AddCluster(new DashboardCluster(
        id: "prod-eu",
        name: "Production (EU-West)",
        dashboardStorage: euStorage,
        isReadOnly: true));

    options.AddCluster(new DashboardCluster(
        id: "staging",
        name: "Staging Cluster",
        dashboardStorage: stagingStorage,
        isReadOnly: false));
});

// Standalone
builder.Services.AddNexJobStandaloneDashboard(options =>
{
    options.Port = 5005;
    options.Title = "Ops Federation Hub";
    options.DisableWorkers = true;
    options.AddCluster(new DashboardCluster("cluster-a", "Billing Cluster", billingStorage));
    options.AddCluster(new DashboardCluster("cluster-b", "Logistics Cluster", logisticsStorage));
});
```

When two or more clusters are registered a **Cluster Switcher** dropdown appears automatically in the top header. The active cluster is controlled via the `?cluster={id}` query parameter and defaults gracefully to the first registered cluster. Read-only clusters reject Run Now, Requeue, Delete, and Pause/Resume actions with `403 Forbidden`.

---

## Queue Scoping for Multi-Service Architectures

When multiple microservices share a storage backend, scope each dashboard instance to its own queues so navigation counters and job lists only show relevant data:

```csharp
// ASP.NET Core — billing service
app.UseNexJobDashboard("/dashboard", options =>
{
    options.Title = "Billing Service Dashboard";
    options.Queues = ["billing", "invoices"];
});

// Standalone — inventory ops host (no workers)
builder.Services.AddNexJobStandaloneDashboard(options =>
{
    options.Title = "Inventory Ops";
    options.Queues = ["inventory", "warehouse"];
    options.DisableWorkers = true;
});
```
