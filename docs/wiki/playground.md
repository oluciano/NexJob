---
title: "NexJob Live Playground: Try the Dashboard in Your Browser"
sidebarTitle: "Live Playground"
description: "Try NexJob in your browser on the live playground: trigger order batches, retries, dead-letter jobs, bursts and recurring sweeps, or run your own copy."
---

# Live Playground

Experience NexJob directly in your browser without spinning up local databases or installing any packages.

[🎮 Launch Live Playground](https://nexjob-playground.fly.dev/){ .md-button .md-button--primary target="_blank" rel="noopener noreferrer" }

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

## Enabling the playground in your own app

The scenario simulator is **disabled by default** (`EnablePlayground = false`). While it is disabled, the Scenarios button is hidden and the scenario endpoint is not served. Opt in for local development or staging:

```csharp
options.EnablePlayground = builder.Environment.IsDevelopment();
```

Set it inside the dashboard options of either host (`UseNexJobDashboard` for ASP.NET Core, `AddNexJobStandaloneDashboard` for a Worker Service). The full setup, the themes and every other option are in the [Dashboard guide](integrations/dashboard.md#configuration-options).

---

## Deploying your own playground

The live site is the repository's own [`Dockerfile`](https://github.com/oluciano/NexJob/blob/develop/Dockerfile), which publishes the `NexJob.Sample.WorkerService` sample, with its Fly.io settings in [`deploy/fly.toml`](https://github.com/oluciano/NexJob/blob/develop/deploy/fly.toml). Use them as they are, or as a starting point for another platform. The image sets `NexJob__Dashboard__LocalhostOnly=false` so the dashboard is reachable from outside the container.

!!! warning

    `LocalhostOnly=false` exposes the dashboard on every interface. Anyone who can reach the port can read job payloads and run actions, including the scenarios. Use it only for a demo with no real data, or register an `IDashboardAuthorizationHandler` (see [Dashboard Authorization](integrations/dashboard.md#dashboard-authorization)).
