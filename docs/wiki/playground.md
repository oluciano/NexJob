---
title: "NexJob Live Playground: Interactive Background Job Schedulers"
sidebarTitle: "Live Playground"
description: "Try NexJob in your browser with our live interactive playground on Fly.io. Simulate load bursts, outages, dead-letter exhausts, and queue stalls."
---

# Live Playground

Experience NexJob directly in your browser without spinning up local databases or installing any packages.

<div style="text-align: center; margin: 2rem 0;">
  <a href="https://nexjob-playground.fly.dev/" target="_blank" rel="noopener noreferrer" class="md-button md-button--primary" style="font-size: 1.1rem; padding: 0.8rem 2rem;">
    🎮 Launch Live Playground
  </a>
</div>

The playground is a live deployment of the **NexJob Standalone Dashboard** running in an in-memory cluster on [Fly.io](https://nexjob-playground.fly.dev/).

---

## Interactive Scenario Simulator

The playground features a dedicated **Scenarios** drawer (accessible from the top header) that lets you trigger real runtime events with a single click:

<div class="grid cards" markdown>

-   **⚡ Simulate Burst Load**

    Enqueues 100 fast background jobs into the `orders` queue. Watch the dispatcher spin up concurrent workers, process jobs in sub-millisecond cycles, and update metrics live.

-   **⚠️ Simulate Outage (Retries)**

    Enqueues 5 jobs that throw transient simulated HTTP 500 errors. Watch NexJob catch the failures, schedule exponential backoff retries, and resume execution.

-   **💀 Spike Dead-Letter**

    Enqueues 3 unhandled fatal exceptions. Observe the jobs exhaust all retry attempts and move automatically to the dead-letter queue with full stack traces and payload inspection.

-   **⏸️ Simulate Queue Stall**

    Pauses the `reports` queue with `IJobControlService` and enqueues 15 heavy report jobs. Inspect the queue buffer accumulation and unpause on demand to watch workers catch up.

-   **⏰ Expire Deadlines**

    Enqueues a job with a 5-second deadline into an unserviced queue. Watch NexJob enforce deadline limits, prevent execution, and flag the job as expired without zombie processes.

</div>

---

## Enabling Playground in Local Development

For security and operational integrity, the scenario simulator is **disabled by default** (`EnablePlayground = false`). Any scenario trigger requests sent when disabled receive a `403 Forbidden` response.

You can safely opt in to the playground during local development or staging:

=== "ASP.NET Core Web App"

    ```csharp
    using NexJob;
    using NexJob.Dashboard;

    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddNexJob();

    var app = builder.Build();

    app.UseNexJobDashboard("/dashboard", options =>
    {
        // Opt in to the interactive scenarios drawer (dev/staging only)
        options.EnablePlayground = app.Environment.IsDevelopment();
        options.DefaultTheme = "semi-dark";
    });

    app.Run();
    ```

=== "Worker Service / Standalone"

    ```csharp
    using NexJob;
    using NexJob.Dashboard.Standalone;

    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddNexJob();

    builder.Services.AddNexJobStandaloneDashboard(options =>
    {
        options.Port = 5005;
        options.EnablePlayground = builder.Environment.IsDevelopment();
        options.DefaultTheme = "semi-dark";
    });

    var host = builder.Build();
    host.Run();
    ```

---

## Deploying Your Own Playground

You can containerize and deploy your own playground instance using Docker and cloud platforms like Fly.io:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
COPY . .
RUN dotnet publish samples/NexJob.Sample.WorkerService -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine
WORKDIR /app
RUN apk add --no-cache tzdata icu-libs
COPY --chown=$APP_UID:$APP_UID --from=build /app/publish .
USER $APP_UID
ENV PORT=8080
ENV NexJob__Dashboard__LocalhostOnly=false
EXPOSE 8080
ENTRYPOINT ["dotnet", "NexJob.Sample.WorkerService.dll"]
```
