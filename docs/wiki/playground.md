---
title: "NexJob Live Playground: Try the Dashboard in Your Browser"
sidebarTitle: "Live Playground"
description: "Try NexJob in your browser on the live playground: trigger order batches, retries, dead-letter jobs, bursts and recurring sweeps, or run your own copy."
---

# Live Playground

Experience NexJob directly in your browser without spinning up local databases or installing any packages.

<div style="text-align: center; margin: 2rem 0;">
  <a href="https://nexjob-playground.fly.dev/" target="_blank" rel="noopener noreferrer" class="md-button md-button--primary" style="font-size: 1.1rem; padding: 0.8rem 2rem;">
    🎮 Launch Live Playground
  </a>
</div>

The playground is a live deployment of the **NexJob Standalone Dashboard** running on [Fly.io](https://nexjob-playground.fly.dev/), with a Scenarios drawer that enqueues real jobs so you can watch them move through the dashboard.

---

## Interactive Scenario Simulator

The playground features a dedicated **Scenarios** drawer (accessible from the top header) that triggers real runtime events with a single click:

<div class="grid cards" markdown>

-   **Order Batch**

    Enqueues order jobs (one, or a batch of five) and lets you follow them from `Enqueued` to `Succeeded` on the Overview and Jobs pages.

-   **Retry Recovery**

    Enqueues a flaky API job that fails on its first attempt and succeeds on a retry. The dashboard opens the job detail so you can inspect the failed attempt and the recovery.

-   **Dead-Letter**

    Enqueues a job that fails for good. The dashboard opens its detail page, where the execution timeline shows the retry budget exhausted and the move to dead-letter.

-   **Concurrency Burst**

    Enqueues 20 order jobs at once, tagged `traffic-spike`, so you can watch the workers take them concurrently and the metrics update live.

-   **Recurring Sweep**

    Makes every recurring job due immediately, so the scheduler fires them on its next cycle.

</div>

Each scenario is a `POST` to `{pathPrefix}/api/scenarios/{scenario}` on the dashboard.

---

## Enabling Playground in Local Development

For security and operational integrity, the scenario simulator is **disabled by default** (`EnablePlayground = false`). When it is disabled, the Scenarios button is hidden and the scenario endpoint is not served.

You can opt in during local development or staging:

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

`DefaultTheme` sets the theme used when the browser has no stored preference: `blue-theme` (default), `semi-dark`, `dark`, `light` or `bordered-theme`. See [Dashboard configuration options](integrations/dashboard.md#configuration-options).

---

## Deploying Your Own Playground

You can containerize and deploy your own playground instance using Docker and cloud platforms like Fly.io. This Dockerfile publishes the `NexJob.Sample.WorkerService` sample from the NexJob repository:

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

!!! warning

    `LocalhostOnly=false` exposes the dashboard on every interface. Anyone who can reach the port can read job payloads and run actions, including the scenarios. Use it only for a demo with no real data, or register an `IDashboardAuthorizationHandler` (see [Dashboard Authorization](integrations/dashboard.md#dashboard-authorization)).
