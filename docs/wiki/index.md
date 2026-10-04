---
title: "NexJob: Background Jobs for .NET"
sidebarTitle: "Home"
description: "NexJob is a lightweight .NET background job scheduler with built-in retries, deadlines, real-time dashboard, and zero paid add-ons."
---

NexJob is a reliable background job processing library for .NET 8 that gives you predictable execution, built-in retries, deadline enforcement, and real-time operational visibility — all without paid storage providers or hidden complexity. Unlike Hangfire, every storage provider (PostgreSQL, SQL Server, Redis, MongoDB, and InMemory) is free, deadlines are first-class, OpenTelemetry is built in, and enqueue performance runs **2.7× faster** with **81% less memory** allocated per operation.

<div class="grid cards" markdown>
  -   [**Quick Start**](quickstart.md)

    Install NexJob and run your first job in under 5 minutes.


  -   [**Mental Model**](mental-model.md)

    Understand storage-first design, state machines, and crash recovery.


  -   [**Storage Providers**](storage/overview.md)

    Choose from PostgreSQL, SQL Server, Redis, MongoDB, or InMemory.


  -   [**Dashboard**](integrations/dashboard.md)

    Real-time cluster visibility with zero external dependencies.


  -   [**Delivery Guarantees**](concepts/delivery-guarantees.md)

    What each failure costs a job: crashes, shutdown, throttling, pauses and deadlines.


  -   [**Circuit Breaker**](guides/circuit-breaker.md)

    Pause a queue automatically when a dependency is down, and ramp back up safely.


  -   [**Alerts**](guides/alerts.md)

    Know when a job fails for good, with a ready Slack recipe.


  -   [**How We Test**](reference/how-we-test.md)

    Real databases, killed processes and upgrades, and what is not covered.

</div>

## Get up and running


  #### Install NexJob

Add the core package to your .NET 8 project:

    ```bash
    dotnet add package NexJob
    ```

    Optionally add a persistent storage provider:

    ```bash
    dotnet add package NexJob.Postgres
    ```

  #### Register services

Call `AddNexJob()` in your `Program.cs` and scan your assembly for job types:

    ```csharp
    using NexJob;
    
    builder.Services.AddNexJob()
                   .AddNexJobJobs(typeof(Program).Assembly);
    ```

  #### Define a job

Implement `IJob` for parameterless jobs or `IJob<T>` for jobs that carry input:

    ```csharp
    public sealed class SendWelcomeEmailJob : IJob<SendWelcomeEmailInput>
    {
        private readonly IEmailService _email;
    
        public SendWelcomeEmailJob(IEmailService email) => _email = email;
    
        public async Task ExecuteAsync(SendWelcomeEmailInput input, CancellationToken ct)
            => await _email.SendAsync(input.Email, "Welcome!", ct);
    }
    
    public sealed record SendWelcomeEmailInput(string Email, string UserName);
    ```

  #### Enqueue and run

Resolve `IScheduler` and enqueue your job from any service, controller, or minimal API handler:

    ```csharp
    var scheduler = app.Services.GetRequiredService<IScheduler>();
    
    await scheduler.EnqueueAsync<SendWelcomeEmailJob, SendWelcomeEmailInput>(
        new SendWelcomeEmailInput("user@example.com", "Jane"),
        deadlineAfter: TimeSpan.FromMinutes(5));
    ```

    The dispatcher picks up the job immediately. If the job is not started within 5 minutes it is marked `Expired` — no silent failures, no zombie jobs.



## Key features

<div class="grid cards" markdown>
  -   **Predictable Retries**

    Configurable global delay policy plus per-job `[Retry]` with exponential backoff. Exhausted retries automatically invoke your dead-letter handler.


  -   **Deadline Enforcement**

    Pass `deadlineAfter` at enqueue time. Jobs that are not started before the deadline are marked `Expired` — eliminating zombie jobs and stale side effects.


  -   **Real-Time Dashboard**

    Built-in dark UI with cluster topology map, live SSE log stream, job catalog, and on-demand ad-hoc triggering. Zero external dependencies.


  -   **Concurrency Throttling**

    Apply `[Throttle]` per resource to cap local concurrency. Add `AddNexJobDistributedThrottle()` for cluster-wide rate limits enforced through Redis.


  -   **Recurring Jobs**

    Register recurring jobs in code or declare them entirely in `appsettings.json` with timezone support — no cron syntax boilerplate in your startup code.


  -   **OpenTelemetry**

    Distributed traces and metrics are emitted out of the box via `NexJob.OpenTelemetry`. No plugins, no wrappers — just wire up your exporter.

</div>
