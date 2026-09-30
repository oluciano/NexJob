# NexJob.OpenTelemetry

OpenTelemetry instrumentation for NexJob — capture spans and metrics via the OpenTelemetry SDK.

## Installation

Add the `NexJob.OpenTelemetry` package to your project.

## Usage

Register NexJob instrumentation with the OpenTelemetry SDK in your `Program.cs`:

```csharp
using NexJob.OpenTelemetry;

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddNexJobInstrumentation()        // ← registers NexJob spans
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddNexJobInstrumentation()        // ← registers NexJob counters/histograms
        .AddOtlpExporter());
```

## Available Spans (Tracing)

- `nexjob.enqueue` — Fired when a job is enqueued.
- `nexjob.execute` — Fired when a job is executed; its parent is the W3C `traceparent` stored on the job when it was enqueued.
- `nexjob.recurring.register` — Fired when a recurring job is registered.

Span tags: `nexjob.job_type`, `nexjob.queue`, `nexjob.job_id` and, on executions, `nexjob.attempt`. The outcome of an execution is the activity status (`Ok` or `Error`).

## Available Metrics

- `nexjob.jobs.enqueued` (counter) — Number of jobs enqueued, tagged by `nexjob.job_type` and `nexjob.queue`.
- `nexjob.jobs.succeeded` (counter) — Number of jobs completed successfully, tagged by `nexjob.job_type`.
- `nexjob.jobs.failed` (counter) — Failed executions, one per failed attempt (including attempts that will be retried), tagged by `nexjob.job_type`.
- `nexjob.jobs.expired` (counter) — Number of jobs that expired before execution, tagged by `nexjob.job_type`.
- `nexjob.job.duration` (histogram, ms) — Job execution duration in milliseconds, tagged by `nexjob.job_type` and `nexjob.status`.
- `nexjob.queue.depth` (observable gauge) — Enqueued jobs waiting per queue, tagged by `nexjob.queue`; useful for Kubernetes HPA/KEDA autoscaling.
- `nexjob.workers.active` (observable gauge) — Workers currently executing jobs on the node.
- `nexjob.workers.total` (observable gauge) — Worker slots configured on the node.

## Compatibility

Works with any OpenTelemetry exporter, including:
- OTLP (Collector, Honeycomb, Lightstep, etc.)
- Jaeger / Zipkin
- Prometheus
- Azure Application Insights
- AWS CloudWatch
- Google Cloud Monitoring
