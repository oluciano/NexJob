# NexJob.Dashboard.Standalone

Self-hosted embedded monitoring dashboard for **NexJob** in **Worker Services** and **Console Applications**.

Enables complete operational monitoring, cluster topology visualization, and queue management in headless background services without requiring a full ASP.NET Core web host or external web server. Features the enterprise **Maxton Design System** with 5 switchable themes (persisted in `localStorage`), 64px Top Header with shortcut search (`Ctrl + K`), Cluster Pipeline Topology Map, real-time SSE live log streaming, and Event Triggers / Listeners visibility.

---

## Installation

```bash
dotnet add package NexJob.Dashboard.Standalone
```

---

## Quick Start

In your Worker Service `Program.cs`:

```csharp
using NexJob;
using NexJob.Dashboard.Standalone;

var builder = Host.CreateApplicationBuilder(args);

// 1. Configure Storage & NexJob
builder.Services.AddNexJobPostgres(builder.Configuration.GetConnectionString("NexJob")!);
builder.Services.AddNexJob();

// 2. Add Standalone Dashboard
builder.Services.AddNexJobStandaloneDashboard(options =>
{
    options.Port = 5005;
    options.Path = "/dashboard";
    options.Title = "Order Processing Worker";
    options.LocalhostOnly = true;
});

var host = builder.Build();
host.Run();
```

When the worker starts, the embedded dashboard is immediately accessible at:
```
http://localhost:5005/dashboard
```

---

## Configuration via `appsettings.json`

You can also bind options directly from your application configuration:

```csharp
builder.Services.AddNexJobStandaloneDashboard(builder.Configuration);
```

```json
{
  "NexJob": {
    "Dashboard": {
      "Port": 5005,
      "Path": "/dashboard",
      "Title": "Worker Service Dashboard",
      "LocalhostOnly": true,
      "PollIntervalSeconds": 3
    }
  }
}
```

---

## Configuration Options

| Option | Type | Default | Description |
|---|---|---|---|
| `Port` | `int` | `5005` | Port number the embedded HTTP server listens on |
| `Path` | `string` | `"/dashboard"` | URL path prefix where the dashboard is mounted |
| `Title` | `string` | `"NexJob"` | Title displayed in the browser tab and navigation bar |
| `LocalhostOnly` | `bool` | `true` | When `true` (default), binds strictly to `localhost`. Set `false` to listen on all interfaces (needed in a container) and register an `IDashboardAuthorizationHandler` |
| `PollIntervalSeconds` | `int` | `3` | SSE live stream update interval in seconds |
| `DisableWorkers` | `bool` | `false` | When `true`, sets `NexJobOptions.Workers = 0` to run as a dedicated ops/monitoring container |
| `DefaultTheme` | `string` | `"blue-theme"` | Color theme used when the browser has no stored preference: `blue-theme`, `semi-dark`, `dark`, `light` or `bordered-theme` |
| `EnablePlayground` | `bool` | `false` | Enables the interactive playground drawer with demo scenarios. Off by default for production safety |
| `Queues` | `IReadOnlyList<string>?` | `null` | Optional list of queues to scope the dashboard view, nav counters, and default job listings |
| `Clusters` | `IReadOnlyList<DashboardCluster>` | `[]` | Registered federated clusters for multi-cluster operations |

---

## Dedicated Ops Host Mode (Dashboard Only)

In high-concurrency environments or multi-service clusters, you can run a dedicated lightweight container strictly for ops/monitoring without taking worker execution slots from processing nodes:

```csharp
builder.Services.AddNexJobStandaloneDashboard(options =>
{
    options.Port = 5005;
    options.Path = "/dashboard";
    options.Title = "Ops Control Center";
    options.DisableWorkers = true; // Sets Workers = 0 on this host
    options.Queues = ["payments", "billing"]; // Scopes UI exclusively to these queues
});
```

---

## Multi-Cluster Federation

You can register multiple clusters to aggregate and manage distinct clusters from a single standalone monitoring instance:

```csharp
builder.Services.AddNexJobStandaloneDashboard(options =>
{
    options.Port = 5005;
    options.Path = "/dashboard";
    options.Title = "Ops Federation Hub";
    options.DisableWorkers = true;

    options.AddCluster(new DashboardCluster("prod", "Production Cluster", prodStorage, isReadOnly: true));
    options.AddCluster(new DashboardCluster("staging", "Staging Cluster", stagingStorage, isReadOnly: false));
});
```

---

## Security Best Practices

- **Secure by default:** the dashboard listens on loopback only (`LocalhostOnly = true`). There is no option to bind to a single network interface: the alternative is all interfaces.
- **Exposing it (containers):** set `LocalhostOnly = false` **and** register an `IDashboardAuthorizationHandler` in your host. It is enforced in standalone mode, but the embedded server has no authentication middleware, so `context.User` is never authenticated: the handler must authenticate from `context.Request` (see the Basic example in the [Dashboard guide](https://github.com/oluciano/NexJob/blob/main/docs/wiki/10-Dashboard.md#authorization-in-the-standalone-dashboard)). Without a handler, anyone who can reach the port can read job payloads and run actions, and NexJob logs a warning at startup.
- **Dedicated Ops Host:** Set `DisableWorkers = true` so the monitoring container does not consume processing capacity or compete for job dispatching.
- **Reverse Proxy:** Place an authenticated reverse proxy (Nginx, Traefik, AWS ALB) in front of the dashboard port when accessing across internal networks.
