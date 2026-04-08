# OpenTelemetry

NexJob emits traces and metrics out of the box. Configure your OpenTelemetry SDK to collect them.

---

## Traces

NexJob uses `ActivitySource` named `"NexJob"`.

### Enqueue Span

```
nexjob.enqueue (Producer)
  ├─ job.type: SendEmailJob
  ├─ job.queue: default
  ├─ job.id: {guid}
  └─ messaging.operation: publish
```

Created when `EnqueueAsync`, `ScheduleAsync`, or `ScheduleAtAsync` is called.

### Execution Span

```
nexjob.execute (Consumer)
  ├─ job.type: SendEmailJob
  ├─ job.queue: default
  ├─ job.id: {guid}
  ├─ job.attempt: 1
  ├─ job.status: Succeeded
  └─ messaging.operation: process
```

Created for each job execution. Includes status (`Succeeded`, `Failed`, `Expired`) and attempt number.

### Recurring Registration Span

```
nexjob.recurring.register (Internal)
  ├─ recurring.count: 5
  └─ messaging.operation: create
```

Created once at startup when recurring jobs are registered.

### Trace Propagation

W3C `traceparent` context is propagated from enqueue to execution. When you call `EnqueueAsync`, the current activity context is stored in the `JobRecord`. When the dispatcher executes the job, it restores the context, creating a child span.

This means you can trace a job from its HTTP request origin through the entire job execution in your APM tool.

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

## Configuration

### Enable NexJob Telemetry

NexJob telemetry is always active. Configure your app's OpenTelemetry pipeline to collect it.

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("NexJob") // Collect NexJob spans
            .AddAspNetCoreInstrumentation()
            .AddOtlpExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter("NexJob") // Collect NexJob metrics
            .AddAspNetCoreInstrumentation()
            .AddOtlpExporter();
    });
```

---

## Example: Correlating HTTP Request to Job Execution

```csharp
// HTTP endpoint enqueues a job
app.MapPost("/orders/{id}/process", async (Guid id, IScheduler scheduler, HttpContext ctx) =>
{
    // The current Activity flows into the job
    await scheduler.EnqueueAsync<ProcessOrderJob, ProcessOrderInput>(
        new ProcessOrderInput(id),
        cancellationToken: ctx.RequestAborted);
});
```

In your APM tool (Jaeger, Zipkin, Application Insights), you'll see:

```
POST /orders/{id}/process (HTTP span)
  └─ nexjob.enqueue (Producer span)
       └─ nexjob.execute (Consumer span) — executed by dispatcher
            └─ Your job's internal spans (if you create them)
```

---

## Next Steps

- [Dashboard](10-Dashboard.md) — See job status in the UI
- [Best Practices](13-Best-Practices.md) — Monitoring guidelines
- [Troubleshooting](16-Troubleshooting.md) — Diagnose issues with telemetry data
