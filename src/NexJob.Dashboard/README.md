# NexJob.Dashboard

Embedded real-time monitoring dashboard middleware for **NexJob** in ASP.NET Core applications.

Provides a lightweight, zero-dependency web interface for monitoring background jobs, active worker nodes, queue throughput, and recurring cron schedules with live updates.

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

- **Live Overview:** Real-time counters for `Enqueued`, `Processing`, `Succeeded`, `Failed`, `Expired`, `Retrying`, and `Dead-Letter` jobs via Server-Sent Events (SSE).
- **Job Details & Inspection:** View serialized input arguments, execution history, exception stack traces, and captured console logs.
- **Queue Management:** Inspect active queues, monitor worker capacity, and dynamically pause/resume processing per queue.
- **Recurring Jobs:** Monitor cron schedules, last execution timestamp, next scheduled run, and manually trigger recurring jobs on demand.
- **Worker Nodes & Heartbeats:** Live view of registered worker instances, process IDs, and health status.
- **Read Replica Isolation:** Compatible with `UseDashboardReadReplica()` to ensure monitoring traffic does not impact primary database write performance.

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
