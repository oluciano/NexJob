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
- `nexjob.execute` — Fired when a job is executed (links to enqueue span via W3C traceparent).
- `nexjob.recurring.register` — Fired when a recurring job is registered.

## Available Metrics

- `nexjob.jobs.enqueued` (counter) — Number of jobs enqueued, tagged by `job_type` and `queue`.
- `nexjob.jobs.succeeded` (counter) — Number of jobs completed successfully.
- `nexjob.jobs.failed` (counter) — Number of jobs that failed.
- `nexjob.jobs.expired` (counter) — Number of jobs that expired before execution.
- `nexjob.job.duration` (histogram, ms) — Job execution duration in milliseconds.

## Compatibility

Works with any OpenTelemetry exporter, including:
- OTLP (Collector, Honeycomb, Lightstep, etc.)
- Jaeger / Zipkin
- Prometheus
- Azure Application Insights
- AWS CloudWatch
- Google Cloud Monitoring
