---
title: "NexJob: Background Jobs for .NET"
sidebarTitle: "Home"
description: "NexJob is a lightweight .NET background job scheduler with built-in retries, deadlines, real-time dashboard, and zero paid add-ons."
---

NexJob is a background job library for .NET 8 and .NET 10, built for jobs that **must run, fail safely and leave a trace**: retries, dead-letter handling, deadlines and a built-in dashboard, on five storage providers (PostgreSQL, SQL Server, Redis, MongoDB and in-memory) with no paid tier. Enqueue is fast and light on memory ([the numbers](introduction.md#benchmark-numbers)).

```bash
dotnet add package NexJob
```

## Start here

<div class="grid cards" markdown>
  -   [**Quick Start**](quickstart.md)

    Install NexJob and run your first job in about five minutes.

  -   [**Live Playground**](playground.md)

    Try the dashboard in your browser: trigger burst jobs, outages and retries in real time.

  -   [**Introduction**](introduction.md)

    Why NexJob, every package, and every feature in one place.

</div>

## Understand

<div class="grid cards" markdown>
  -   [**Mental Model**](mental-model.md)

    Storage-first design, the job state machine and crash recovery.

  -   [**Delivery Guarantees**](concepts/delivery-guarantees.md)

    What each failure costs a job: crashes, shutdown, throttling, pauses and deadlines.

  -   [**Retries and dead letter**](concepts/retries-and-dead-letter.md)

    Exponential backoff, and what happens to a job when every attempt is spent.

  -   [**Scheduling**](concepts/scheduling.md)

    Enqueue, delay, set deadlines and chain job continuations.

  -   [**Storage Providers**](storage/overview.md)

    Choose between PostgreSQL, SQL Server, Redis, MongoDB and in-memory.

  -   [**How We Test**](reference/how-we-test.md)

    Real databases, killed processes and upgrades, and what is not covered.

</div>

## Operate

<div class="grid cards" markdown>
  -   [**Dashboard**](integrations/dashboard.md)

    Real-time cluster visibility with no external dependencies.

  -   [**Alerts**](guides/alerts.md)

    Know when a job fails for good, with a ready Slack recipe.

  -   [**Circuit Breaker**](guides/circuit-breaker.md)

    Pause a queue automatically when a dependency is down, and ramp back up safely.

</div>

<img referrerpolicy="no-referrer-when-downgrade" src="https://static.scarf.sh/a.png?x-pxid=088bbff5-783b-4a01-aab6-c4f00637fe56" />
