# Dashboard

Monitor, debug, and manage jobs through a built-in dark UI.

---

## ASP.NET Core

```csharp
var app = builder.Build();

// Enable dashboard at /dashboard
app.UseNexJobDashboard();

// Or custom path and options
app.UseNexJobDashboard("/jobs", options =>
{
    options.Title = "My Jobs";
    options.PollIntervalSeconds = 5;
});

app.Run();
```

---

## Standalone (Worker Services)

For Worker Services without ASP.NET Core, use the standalone dashboard.

```csharp
builder.Services.AddNexJobStandaloneDashboard();
```

The dashboard runs an embedded HTTP server on `http://localhost:5005/dashboard`.

Configure the port:

```csharp
builder.Services.AddNexJobStandaloneDashboard(options =>
{
    options.Port = 8080;
    options.Host = "0.0.0.0";
});
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
