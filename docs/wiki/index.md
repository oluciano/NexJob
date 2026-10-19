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

  -   [**How NexJob is tested**](reference/how-we-test.md)

    How NexJob's own test suite works: real databases, killed processes and upgrades, and what is not covered.

  -   [**Writing Tests**](guides/writing-tests.md)

    Test your own jobs: unit tests with fakes and integration tests on InMemory storage.

</div>

## Build

<div class="grid cards" markdown>
  -   [**Recurring jobs**](concepts/recurring-jobs.md)

    Cron schedules from code or `appsettings.json`.

  -   [**Queues**](concepts/queues.md)

    Route jobs, choose which queues a host polls, and pause or resume them.

  -   [**Throttling**](guides/throttling.md)

    Cap concurrent executions per resource, per process or cluster-wide.

  -   [**Idempotency**](concepts/idempotency.md)

    Make a job safe to run more than once.

  -   [**Triggers**](integrations/triggers.md)

    Enqueue jobs from Kafka, RabbitMQ, SQS, Service Bus, Pub/Sub and Salesforce.

  -   [**Multi-Service**](guides/multi-service.md)

    Several services on one database without stepping on each other.

</div>

## Operate

<div class="grid cards" markdown>
  -   [**Dashboard**](integrations/dashboard.md)

    Real-time cluster visibility with no external dependencies.

  -   [**Alerts**](guides/alerts.md)

    Know when a job fails for good, with a ready Slack recipe.

  -   [**Circuit Breaker**](guides/circuit-breaker.md)

    Pause a queue automatically when a dependency is down, and ramp back up safely.

  -   [**Runtime Control**](guides/runtime-control.md)

    Pause, resume, requeue and delete from code.

  -   [**OpenTelemetry**](integrations/opentelemetry.md)

    Traces and metrics for every execution.

  -   [**Production checklist**](guides/best-practices.md#production-checklist)

    What to verify before you go live.

</div>

## Solve a problem

<div class="grid cards" markdown>
  -   [**Troubleshooting**](reference/troubleshooting.md)

    Jobs not picked up, stuck in Processing, handlers not called.

  -   [**FAQ**](reference/faq.md)

    Restarts, duplicates, retention, storage and Hangfire comparison.

  -   [**Migration**](reference/migration.md)

    Breaking changes and what to update, version by version.

</div>

<img referrerpolicy="no-referrer-when-downgrade" src="https://static.scarf.sh/a.png?x-pxid=088bbff5-783b-4a01-aab6-c4f00637fe56" />
