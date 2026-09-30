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

- **`nexjob.enqueue`** (Producer) — Fired when a job is enqueued via `EnqueueAsync`, `ScheduleAsync`, `ScheduleAtAsync` or `ContinueWithAsync`.
  - `nexjob.job_type`: Assembly-qualified name of the job type.
  - `nexjob.queue`: Target queue name.
  - `nexjob.job_id`: Unique identifier of the job.
  - Extra tags depending on the call: `nexjob.delay_seconds`, `nexjob.scheduled_at`, `nexjob.parent_job_id`.
- **`nexjob.execute`** (Consumer) — Fired for each job execution. Its parent is the trace context stored on the job when it was enqueued (W3C `traceparent`).
  - `nexjob.job_type`: Job type name.
  - `nexjob.queue`: Queue name.
  - `nexjob.job_id`: Job ID.
  - `nexjob.attempt`: Current attempt number.
  - The outcome is the activity status: `Ok` when the job succeeded, `Error` (with an `exception` event) when it failed. `nexjob.foreign_job` and `nexjob.interrupted` are set to `true` when the job was deferred because its type belongs to another service, or interrupted by a host shutdown and requeued.
- **`nexjob.recurring.register`** (Internal) — Fired when a recurring job is registered with `RecurringAsync`.
  - `nexjob.job_type`: Job type name.
  - `nexjob.recurring_job_id`: The recurring job id.
  - Also `nexjob.queue`, `nexjob.cron` and `nexjob.next_execution`.

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
| `nexjob.jobs.failed` | Counter | Failed executions: one per failed attempt, including attempts that will be retried |
| `nexjob.jobs.expired` | Counter | Total jobs expired (deadline exceeded) |
| `nexjob.job.duration` | Histogram | Job execution time in milliseconds |
| `nexjob.queue.depth` | ObservableGauge | Current number of enqueued jobs waiting in the queue (tagged with `nexjob.queue`) |
| `nexjob.workers.active` | ObservableGauge | Number of workers currently executing jobs |
| `nexjob.workers.total` | ObservableGauge | Total number of worker slots configured on the node |

Dimensions: every job counter and the duration histogram carry `nexjob.job_type`; `nexjob.jobs.enqueued` also carries `nexjob.queue`, and `nexjob.job.duration` carries `nexjob.status`. The `nexjob.queue.depth` gauge is tagged with `nexjob.queue`.

### Kubernetes Horizontal Pod Autoscaler (HPA)

The `nexjob.queue.depth` gauge allows autoscaling NexJob worker pods directly from queue backlog using Prometheus and KEDA (Kubernetes Event-driven Autoscaling) or custom metrics:

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

---

## Structured Logging — Ambient Job Context (Built-in, Zero Dependencies)

NexJob automatically enriches every log entry emitted during job execution with a structured `ILogger.BeginScope` — **no additional packages required**. This works out of the box with the standard .NET logging pipeline, Serilog, NLog, and OpenTelemetry logging.

### Ambient Scope Keys

When a job starts executing, `JobExecutor` opens a logging scope containing the following keys:

| Key | Type | Description |
|---|---|---|
| `NexJob.JobId` | `Guid` | The unique job identifier |
| `NexJob.JobType` | `string` | The assembly-qualified job type name |
| `NexJob.Queue` | `string` | The target queue name |
| `NexJob.Attempt` | `int` | The current attempt number (1-based) |
| `NexJob.TraceParent` | `string` | The W3C `traceparent` header (empty string if not propagated) |

Every log line emitted inside `IJob.ExecuteAsync` — or by `JobExecutor` internally — **automatically inherits all 5 keys** as structured fields.

### Sample Structured Log Output

With the default .NET JSON console formatter and `IncludeScopes: true`:

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

### Enabling Structured Scopes

#### .NET Built-in JSON Console Logger

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

#### Serilog

Serilog captures `ILogger.BeginScope` automatically when `Enrich.FromLogContext()` is configured:

```csharp
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()        // ← captures NexJob scope keys
    .WriteTo.Console(new JsonFormatter())
    .WriteTo.Seq("http://localhost:5341")
    .CreateLogger();
```

### Querying in Log Aggregation Platforms

#### Splunk

```spl
index=prod NexJob.JobId="8e3b1c77-4a11-4e92-91cd-32aa812f8112"
```

```spl
index=prod NexJob.Queue="payments" NexJob.Attempt>1 | stats count by NexJob.JobType
```

#### Grafana Loki

```logql
{app="my-worker"} | json | NexJob_Queue="payments" | line_format "{{.NexJob_JobId}} {{.Message}}"
```

```logql
{app="my-worker"} | json | NexJob_Attempt > 1 | count_over_time[5m]
```

#### Datadog

```
@NexJob.Queue:payments @NexJob.Attempt:>1
```

> [!TIP]
> Combine structured log correlation with distributed traces: filter by `NexJob.TraceParent` to jump from a Loki log line directly to the corresponding Tempo/Jaeger span.

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
