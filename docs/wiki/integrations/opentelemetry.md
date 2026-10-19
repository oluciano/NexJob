---
title: "NexJob OpenTelemetry: Traces, Metrics, and Logging"
sidebarTitle: "OpenTelemetry"
description: "Instrument NexJob with OpenTelemetry to capture job execution traces and metrics for distributed tracing, HPA scaling, and observability pipelines."
---

NexJob emits distributed traces and metrics out of the box using the standard .NET `ActivitySource` and `Meter` APIs. The `NexJob.OpenTelemetry` package provides opt-in extension methods to wire those signals into the OpenTelemetry SDK, so you can export them to Jaeger, Zipkin, Prometheus, Azure Application Insights, or any OTLP-compatible backend without writing any custom instrumentation.

---

## Installation

```bash
dotnet add package NexJob.OpenTelemetry
```

---

## Registering instrumentation

Call `AddNexJobInstrumentation()` inside both `WithTracing` and `WithMetrics` in your `Program.cs`:

```csharp
using NexJob.OpenTelemetry;

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddNexJobInstrumentation()        // registers NexJob spans
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddNexJobInstrumentation()        // registers NexJob counters and histograms
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter());
```

You can combine any exporters supported by the OpenTelemetry .NET SDK — OTLP, Jaeger, Zipkin, Prometheus, Application Insights, CloudWatch, and more. NexJob uses the `ActivitySource` named `"NexJob"` and the `Meter` named `"NexJob"`.

---

## Available spans

NexJob produces three span types during normal operation.

### `nexjob.enqueue` — Producer

Fired whenever a job enters storage via `EnqueueAsync`, `ScheduleAsync`, `ScheduleAtAsync`, or `ContinueWithAsync`.

| Attribute | Description |
|---|---|
| `nexjob.job_type` | Full name of the job type, **without** the assembly (the `nexjob.execute` span carries the assembly-qualified name). |
| `nexjob.queue` | Target queue name. |
| `nexjob.job_id` | Unique identifier of the job, set once the job has been stored. |
| `nexjob.delay_seconds` | *(`ScheduleAsync`)* Seconds until the job becomes eligible. |
| `nexjob.scheduled_at` | *(`ScheduleAtAsync`)* ISO 8601 timestamp of the scheduled execution time. |
| `nexjob.parent_job_id` | *(continuations)* ID of the parent job. |

### `nexjob.execute` — Consumer

Fired for each job execution attempt. Its parent is the trace context stored on the job when its record was built, which is the **ambient activity of the caller**, not the `nexjob.enqueue` span (see [trace propagation](#trace-propagation)). That makes an end-to-end trace from an HTTP request through the job's execution.

| Attribute | Description |
|---|---|
| `nexjob.job_type` | Assembly-qualified name of the job type, as stored on the job. |
| `nexjob.queue` | Queue name. |
| `nexjob.job_id` | Job ID. |
| `nexjob.attempt` | Current attempt number (1-based). |
| `nexjob.foreign_job` | `true` when the job type belongs to another service and was deferred. |
| `nexjob.interrupted` | `true` when the job was interrupted by a host shutdown. |
| `nexjob.throttle_deferred` | `true` when the job was returned to the queue because a `[Throttle]` slot was not free. |

The span status is `Ok` when the job succeeds and `Error` when it fails (with an `exception` event). A job that is **deferred as a foreign job** or **interrupted by a shutdown** also ends its span with `Error`, although neither is a failure: filter on `nexjob.foreign_job` and `nexjob.interrupted` before you alert on error spans.

### `nexjob.recurring.register`

Fired when a recurring job is registered via `RecurringAsync`.

| Attribute | Description |
|---|---|
| `nexjob.job_type` | Job type name. |
| `nexjob.recurring_job_id` | The recurring job ID. |
| `nexjob.queue` | Target queue name. |
| `nexjob.cron` | Cron expression used to schedule executions. |
| `nexjob.next_execution` | ISO 8601 timestamp of the next scheduled execution. |

---

## Trace propagation

When you call `EnqueueAsync`, NexJob captures the current W3C `traceparent` from the ambient `Activity` and stores it on the job record. When the dispatcher executes the job it restores that context and creates the `nexjob.execute` span under it, so the full trace, from the originating HTTP request through the background execution, is available in your APM tool.

The same propagation applies to **broker triggers** (Kafka, RabbitMQ, Azure Service Bus, AWS SQS, Google Pub/Sub). Each trigger extracts `traceparent` from the broker message headers or attributes before enqueuing the job, connecting the producing service's trace to the NexJob execution span.

```text
HTTP Request (span A)
  ├─ nexjob.enqueue (span B, child of A)
  └─ nexjob.execute (span C, child of A: runs later on a worker)
```

The stored `traceparent` is the one of the caller at the moment the job record is built, before the `nexjob.enqueue` span starts, so `nexjob.execute` is a **sibling** of `nexjob.enqueue`, not its child. Both are in the same trace. (Recorded with an `ActivityListener` around an `EnqueueAsync` call inside an ambient activity.)

---

## Available metrics

NexJob exposes the following metrics under the `"NexJob"` meter (`NexJobMetrics.MeterName`). The spans come from the activity source of the same name (`NexJobActivitySource.Name`, also `"NexJob"`). Both classes are public:

| Metric | Type | Description |
|---|---|---|
| `nexjob.jobs.enqueued` | Counter | Total jobs enqueued. Tagged with `nexjob.job_type` and `nexjob.queue`. |
| `nexjob.jobs.succeeded` | Counter | Total jobs that completed successfully. Tagged with `nexjob.job_type`. |
| `nexjob.jobs.failed` | Counter | Failed execution attempts — one increment per failed attempt, including attempts that will be retried. Tagged with `nexjob.job_type`. |
| `nexjob.jobs.expired` | Counter | Total jobs that exceeded their deadline and were expired. Tagged with `nexjob.job_type`. |
| `nexjob.dead_letter.forwarded` | Counter | Dead-lettered jobs that an `IDeadLetterForwarder` handed over to its destination. Tagged with `nexjob.forwarder`. See [dead-letter forwarding](../concepts/dead-letter-forwarding.md). |
| `nexjob.dead_letter.forward_failed` | Counter | Forwards that failed; the exception is logged and swallowed, so this counter is the only signal. Tagged with `nexjob.forwarder`. |
| `nexjob.jobs.cancellation_ignored` | Counter | Jobs still running 10 seconds after their execution timeout cancelled their token, so they still hold a worker slot. Tagged with `nexjob.job_type`. |
| `nexjob.jobs.throttle_deferred` | Counter | Jobs returned to the queue because a slot of their `[Throttle]` resource was not free. Tagged with `nexjob.job_type` and `nexjob.resource`. |
| `nexjob.recurring.id_collisions` | Counter | Recurring job registrations that overwrote a job of a different type stored under the same id (two applications sharing a database). Tagged with `recurring_job_id`. See [recurring job ids](../concepts/recurring-jobs.md#recurring-job-ids-are-global-to-the-database). |
| `nexjob.job.duration` | Histogram | Job execution time in milliseconds. Tagged with `nexjob.job_type` and `nexjob.status`. |
| `nexjob.queue.depth` | ObservableGauge | Number of enqueued jobs waiting per queue, **read from storage by every node**: each node reports the same, storage-wide number. Tagged with `nexjob.queue`. |
| `nexjob.workers.active` | ObservableGauge | Number of workers currently executing jobs on **this node** (it differs per node). |
| `nexjob.workers.total` | ObservableGauge | Total number of worker slots configured on this node (`0` on a host with `Workers = 0`). |

The `nexjob.queue` tag carries the stored queue name. Since v6.0 the default queue of an application is `{prefix}.default`, so alerts and dashboards that filter on `default` need the new name (see [the v6.0.0 migration](../reference/migration.md#v5100-v600)).

---

## Kubernetes HPA with queue depth

The `nexjob.queue.depth` gauge lets you autoscale worker pods directly from queue backlog using Prometheus and [KEDA](https://keda.sh/):

```yaml
apiVersion: keda.sh/v1alpha1
kind: ScaledObject
metadata:
  name: nexjob-worker-scaler
spec:
  scaleTargetRef:
    name: nexjob-worker-deployment
  minReplicaCount: 2
  maxReplicaCount: 20
  triggers:
    - type: prometheus
      metadata:
        serverAddress: http://prometheus-k8s.monitoring.svc:9090
        metricName: nexjob_queue_depth
        query: max(nexjob_queue_depth{nexjob_queue="critical"})
        threshold: '50'
```

KEDA scales the deployment up when the `critical` queue depth exceeds 50 waiting jobs, and back down when it drains.

Use `max` (or `avg`), not `sum`: every node reports the same storage-wide depth, so `sum` would multiply it by the number of pods, and scaling up would raise the sum again.

---

## Structured logging: ambient job context

NexJob automatically enriches every log entry emitted during job execution using `ILogger.BeginScope` — no additional packages required. This works with the built-in .NET logging pipeline, Serilog, NLog, and the OpenTelemetry logging bridge.

### Scope keys

| Key | Type | Description |
|---|---|---|
| `NexJob.JobId` | `Guid` | Unique job identifier. |
| `NexJob.JobType` | `string` | Assembly-qualified job type name. |
| `NexJob.Queue` | `string` | Target queue name. |
| `NexJob.Attempt` | `int` | Current attempt number (1-based). |
| `NexJob.TraceParent` | `string` | W3C `traceparent` header (empty string if not propagated). |

Every log line emitted inside `IJob.ExecuteAsync` — or internally by the job executor — automatically inherits all five keys as structured fields.

### Example structured log output

```json
{
  "Timestamp": "2026-09-26T08:45:10.123Z",
  "Level": "Information",
  "Message": "Payment approved for Order #45210",
  "NexJob.JobId": "8e3b1c77-4a11-4e92-91cd-32aa812f8112",
  "NexJob.JobType": "Acme.Billing.ProcessPaymentJob",
  "NexJob.Queue": "payments",
  "NexJob.Attempt": 1,
  "NexJob.TraceParent": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"
}
```

### Enabling scopes with the built-in JSON console logger

```json
{
  "Logging": {
    "Console": {
      "FormatterName": "json",
      "FormatterOptions": {
        "IncludeScopes": true
      }
    }
  }
}
```

### Enabling scopes with Serilog

The logging recipes below (Serilog, the JSON console logger, and the Splunk, Loki and Datadog queries) were not run for this page.

```csharp
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()        // captures NexJob scope keys
    .WriteTo.Console(new JsonFormatter())
    .WriteTo.Seq("http://localhost:5341")
    .CreateLogger();
```

### Querying structured logs


=== "Splunk"

    ```spl
    index=prod NexJob.JobId="8e3b1c77-4a11-4e92-91cd-32aa812f8112"
    ```

    ```spl
    index=prod NexJob.Queue="payments" NexJob.Attempt>1 | stats count by NexJob.JobType
    ```

=== "Grafana Loki"

    ```logql
    {app="my-worker"} | json | NexJob_Queue="payments" | line_format "{{.NexJob_JobId}} {{.Message}}"
    ```

    ```logql
    {app="my-worker"} | json | NexJob_Attempt > 1 | count_over_time[5m]
    ```

=== "Datadog"

    ```text
    @NexJob.Queue:payments @NexJob.Attempt:>1
    ```




!!! tip
    Combine structured log correlation with distributed traces: filter by `NexJob.TraceParent` to jump from a Loki or Splunk log line directly to the corresponding Tempo or Jaeger span.


---

## Compatible exporters

`NexJob.OpenTelemetry` works with any OpenTelemetry exporter:

- **OTLP** — Collector, Honeycomb, Lightstep, and any OTLP-compatible backend
- **Jaeger** and **Zipkin**
- **Prometheus** (scrape endpoint via `AddPrometheusExporter`)
- **Azure Application Insights**
- **AWS CloudWatch**
- **Google Cloud Monitoring**

!!! tip
    To turn these metrics into alerts (jobs that exhausted their attempts, expired jobs, a queue nobody drains), see the [Alerts](../guides/alerts.md) guide.

## Try it

The [`NexJob.Sample.Storage`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.Storage) sample exports NexJob traces and metrics with OpenTelemetry next to a read replica and a distributed throttle.
