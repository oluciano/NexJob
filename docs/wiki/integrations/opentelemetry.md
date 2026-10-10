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

## Registering Instrumentation

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

## Available Spans

NexJob produces three span types during normal operation.

### `nexjob.enqueue` — Producer

Fired whenever a job enters storage via `EnqueueAsync`, `ScheduleAsync`, `ScheduleAtAsync`, or `ContinueWithAsync`.

| Attribute | Description |
|---|---|
| `nexjob.job_type` | Assembly-qualified name of the job type. |
| `nexjob.queue` | Target queue name. |
| `nexjob.job_id` | Unique identifier of the job. |
| `nexjob.delay_seconds` | *(scheduled jobs)* Seconds until the job becomes eligible. |
| `nexjob.scheduled_at` | *(scheduled jobs)* ISO 8601 timestamp of the scheduled execution time. |
| `nexjob.parent_job_id` | *(continuations)* ID of the parent job. |

### `nexjob.execute` — Consumer

Fired for each job execution attempt. Its parent span is the trace context stored on the job at enqueue time, enabling end-to-end traces from an HTTP request all the way through job completion.

| Attribute | Description |
|---|---|
| `nexjob.job_type` | Job type name. |
| `nexjob.queue` | Queue name. |
| `nexjob.job_id` | Job ID. |
| `nexjob.attempt` | Current attempt number (1-based). |
| `nexjob.foreign_job` | `true` when the job type belongs to another service and was deferred. |
| `nexjob.interrupted` | `true` when the job was interrupted by a host shutdown and requeued. |

The span status is set to `Ok` when the job succeeds, or `Error` (with an attached `exception` event) when it fails.

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

## Trace Propagation

When you call `EnqueueAsync`, NexJob captures the current W3C `traceparent` from the ambient `Activity` and stores it on the job record. When the dispatcher executes the job it restores that context and creates a child span, so the full trace — from the originating HTTP request through the background execution — is available in your APM tool.

The same propagation applies to **broker triggers** (Kafka, RabbitMQ, Azure Service Bus, AWS SQS, Google Pub/Sub). Each trigger extracts `traceparent` from the broker message headers or attributes before enqueuing the job, connecting the producing service's trace to the NexJob execution span.

```text
HTTP Request (span A)
  └─ nexjob.enqueue (span B, child of A)
       └─ nexjob.execute (span C, child of B — runs asynchronously on a worker)
```

---

## Available Metrics

NexJob exposes the following metrics under the `"NexJob"` meter:

| Metric | Type | Description |
|---|---|---|
| `nexjob.jobs.enqueued` | Counter | Total jobs enqueued. Tagged with `nexjob.job_type` and `nexjob.queue`. |
| `nexjob.jobs.succeeded` | Counter | Total jobs that completed successfully. Tagged with `nexjob.job_type`. |
| `nexjob.jobs.failed` | Counter | Failed execution attempts — one increment per failed attempt, including attempts that will be retried. Tagged with `nexjob.job_type`. |
| `nexjob.jobs.expired` | Counter | Total jobs that exceeded their deadline and were expired. Tagged with `nexjob.job_type`. |
| `nexjob.dead_letter.forwarded` | Counter | Dead-lettered jobs that an `IDeadLetterForwarder` handed over to its destination. Tagged with `nexjob.forwarder`. See [dead-letter forwarding](../concepts/retries-and-dead-letter.md). |
| `nexjob.dead_letter.forward_failed` | Counter | Forwards that failed; the exception is logged and swallowed, so this counter is the only signal. Tagged with `nexjob.forwarder`. |
| `nexjob.jobs.cancellation_ignored` | Counter | Jobs still running 10 seconds after their execution timeout cancelled their token, so they still hold a worker slot. Tagged with `nexjob.job_type`. |
| `nexjob.jobs.throttle_deferred` | Counter | Jobs returned to the queue because a slot of their `[Throttle]` resource was not free. Tagged with `nexjob.job_type` and `nexjob.resource`. |
| `nexjob.recurring.id_collisions` | Counter | Recurring job registrations that overwrote a job of a different type stored under the same id (two applications sharing a database). Tagged with `recurring_job_id`. See [recurring job ids](../concepts/recurring-jobs.md#recurring-job-ids-are-global-to-the-database). |
| `nexjob.job.duration` | Histogram | Job execution time in milliseconds. Tagged with `nexjob.job_type` and `nexjob.status`. |
| `nexjob.queue.depth` | ObservableGauge | Current number of enqueued jobs waiting per queue. Tagged with `nexjob.queue`. |
| `nexjob.workers.active` | ObservableGauge | Number of workers currently executing jobs on this node. |
| `nexjob.workers.total` | ObservableGauge | Total number of worker slots configured on this node (`0` on a host with `Workers = 0`). |

The `nexjob.queue` tag carries the stored queue name. Since v6.0 the default queue of an application is `{prefix}.default`, so alerts and dashboards that filter on `default` need the new name (see [the v6.0.0 migration](../reference/migration.md#v5100-v600)).

---

## Kubernetes HPA with Queue Depth

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
        query: sum(nexjob_queue_depth{nexjob_queue="critical"})
        threshold: '50'
```

KEDA scales the deployment up when the `critical` queue depth exceeds 50 waiting jobs, and back down when it drains.

---

## Structured Logging — Ambient Job Context

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

## Compatible Exporters

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
