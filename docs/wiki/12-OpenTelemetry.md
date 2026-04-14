# OpenTelemetry

NexJob emits traces and metrics out of the box. The `NexJob.OpenTelemetry` package provides an opt-in extension to register NexJob instrumentation with the OpenTelemetry SDK.

---

## Installation

Add the `NexJob.OpenTelemetry` package to your project:

```bash
dotnet add package NexJob.OpenTelemetry
```

---

## Usage

Register NexJob instrumentation with the OpenTelemetry SDK in your `Program.cs`:

```csharp
using NexJob.OpenTelemetry;

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddNexJobInstrumentation()        // ← registers NexJob spans
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddNexJobInstrumentation()        // ← registers NexJob counters/histograms
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter());
```

---

## Traces

NexJob uses `ActivitySource` named `"NexJob"`.

### Available Spans

- **`nexjob.enqueue`** (Producer) — Fired when a job is enqueued via `EnqueueAsync`, `ScheduleAsync`, or `ScheduleAtAsync`.
  - `job.type`: Assembly-qualified name of the job type.
  - `job.queue`: Target queue name.
  - `job.id`: Unique identifier of the job.
- **`nexjob.execute`** (Consumer) — Fired for each job execution (links to enqueue span via W3C traceparent).
  - `job.type`: Job type name.
  - `job.queue`: Queue name.
  - `job.id`: Job ID.
  - `job.attempt`: Current attempt number.
  - `job.status`: Execution outcome (`Succeeded`, `Failed`, `Expired`).
- **`nexjob.recurring.register`** (Internal) — Fired at startup when recurring jobs are registered.
  - `recurring.count`: Number of recurring jobs registered.

### Trace Propagation

W3C `traceparent` context is propagated from enqueue to execution. When you call `EnqueueAsync`, the current activity context is stored in the `JobRecord`. When the dispatcher executes the job, it restores the context, creating a child span.

This means you can trace a job from its HTTP request origin through the entire job execution in your APM tool (Jaeger, Zipkin, Application Insights).

---

## Metrics

NexJob uses `Meter` named `"NexJob"`.

| Metric | Type | Description |
|---|---|---|
| `nexjob.jobs.enqueued` | Counter | Total jobs enqueued |
| `nexjob.jobs.succeeded` | Counter | Total jobs succeeded |
| `nexjob.jobs.failed` | Counter | Total jobs failed (dead-letter) |
| `nexjob.jobs.expired` | Counter | Total jobs expired (deadline exceeded) |
| `nexjob.job.duration` | Histogram | Job execution time in milliseconds |

All metrics include `job.type` and `job.queue` as dimensions.

---

## Compatibility

`NexJob.OpenTelemetry` works with any OpenTelemetry exporter, including:
- OTLP (Collector, Honeycomb, Lightstep, etc.)
- Jaeger / Zipkin
- Prometheus
- Azure Application Insights
- AWS CloudWatch
- Google Cloud Monitoring

---

## Next Steps

- [Dashboard](10-Dashboard.md) — See job status in the UI
- [Best Practices](13-Best-Practices.md) — Monitoring guidelines
- [Troubleshooting](16-Troubleshooting.md) — Diagnose issues with telemetry data
