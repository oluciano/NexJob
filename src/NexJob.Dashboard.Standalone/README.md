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
| `LocalhostOnly` | `bool` | `false` | When `true`, binds strictly to `127.0.0.1` / `localhost` |
| `PollIntervalSeconds` | `int` | `3` | SSE live stream update interval in seconds |

---

## Security Best Practices

- **Production Workers:** Set `LocalhostOnly = true` to restrict access strictly to the local host machine, or bind to an internal network interface.
- **Reverse Proxy:** Place an authenticated reverse proxy (Nginx, Traefik, AWS ALB) in front of the dashboard port when accessing across internal networks.
