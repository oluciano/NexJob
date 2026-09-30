# IJobContext

Access runtime context inside job execution. Injected via DI, scoped to each job invocation.

---

## Basic Usage

Inject `IJobContext` through the job constructor.

```csharp
public sealed class LongRunningJob : IJob
{
    private readonly IJobContext _context;

    public LongRunningJob(IJobContext context) => _context = context;

    public async Task ExecuteAsync(CancellationToken ct)
    {
        // Access context
        var jobId = _context.JobId;
        var attempt = _context.Attempt;
        var queue = _context.Queue;
        var tags = _context.Tags;

        // Do work...
    }
}
```

---

## Available Properties

| Property | Type | Description |
|---|---|---|
| `JobId` | `JobId` | Unique identifier for this execution |
| `Attempt` | `int` | Current attempt number (1-based) |
| `MaxAttempts` | `int` | Maximum attempts configured for this job |
| `Queue` | `string` | Queue name this job was enqueued to |
| `RecurringJobId` | `string?` | If this is a recurring job, its ID |
| `Tags` | `IReadOnlyList<string>` | Tags attached at enqueue time |

---

## Progress Reporting

Report progress back to storage for dashboard visibility.

```csharp
public sealed class DataImportJob : IJob<ImportInput>
{
    private readonly IJobContext _context;
    private readonly IDataService _data;

    public DataImportJob(IJobContext context, IDataService data)
    {
        _context = context;
        _data = data;
    }

    public async Task ExecuteAsync(ImportInput input, CancellationToken ct)
    {
        var records = await _data.FetchAsync(input.Source, ct);
        var total = records.Count;

        for (var i = 0; i < total; i++)
        {
            await _data.ProcessAsync(records[i], ct);

            var percent = (int)((i + 1) / (double)total * 100);
            await _context.ReportProgressAsync(percent, $"Processed {i + 1}/{total}", ct);
        }
    }
}
```

Progress is visible in the [Dashboard](10-Dashboard.md) and via storage provider queries.

---

## Progress Extensions

Convenience methods for common progress patterns.

### AsyncEnumerable with Progress

```csharp
await foreach (var item in source.WithProgress(_context, ct))
{
    await ProcessAsync(item, ct);
}
```

Reports the percentage of items already yielded. The extension **reads the whole source into memory first** (it needs the total count), so use it for collections that fit in memory, not for very large or unbounded streams.

### IEnumerable with Progress

```csharp
foreach (var item in items.WithProgress(_context))
{
    await ProcessAsync(item, ct);
}
```

Fire-and-forget progress for synchronous collections: the sequence is materialized first to know its size, and the progress call is not awaited, so a storage error while reporting is not surfaced to the job.

---

## Checkpoints & State Saving (Long-Running Jobs)

For long-running batch jobs, data migrations, or multi-step workflows, NexJob supports state checkpoints.
If a job is interrupted (worker restart, transient crash, or retry after failure), you can resume processing from the last recorded checkpoint rather than starting from scratch.

### Saving & Resuming Checkpoints

Use `SaveCheckpointAsync<TState>` to persist your progress and checkpoint state atomically, and `GetCheckpoint<TState>()` to resume.

```csharp
public sealed class LargeDataSyncJob : IJob
{
    private readonly IJobContext _context;
    private readonly IRecordReader _reader;
    private readonly IRecordProcessor _processor;

    public LargeDataSyncJob(IJobContext context, IRecordReader reader, IRecordProcessor processor)
    {
        _context = context;
        _reader = reader;
        _processor = processor;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        // 1. Resume from previous checkpoint if available
        var state = _context.GetCheckpoint<SyncCheckpoint>() ?? new SyncCheckpoint(LastProcessedId: 0, TotalBatches: 0);

        var batch = await _reader.FetchNextBatchAsync(afterId: state.LastProcessedId, ct);
        while (batch.Count > 0)
        {
            await _processor.ProcessBatchAsync(batch, ct);

            state = state with { LastProcessedId = batch.Last().Id, TotalBatches = state.TotalBatches + 1 };

            // 2. Persist checkpoint along with progress reporting
            await _context.SaveCheckpointAsync(
                state: state,
                percent: null,
                message: $"Processed batch {state.TotalBatches} up to ID {state.LastProcessedId}",
                ct: ct);

            batch = await _reader.FetchNextBatchAsync(afterId: state.LastProcessedId, ct);
        }
    }
}

public record SyncCheckpoint(long LastProcessedId, int TotalBatches);
```

### Checkpoint Lifecycle Guarantees

- **State Persistence Across Retries:** If an exception occurs, the job transitions to `Scheduled` (for retry) or `Failed` (dead-letter) with its `CheckpointJson` intact. When attempt 2 starts, `_context.GetCheckpoint<TState>()` returns the exact state saved prior to failure.
- **Auto-Clearing on Success:** When the job successfully completes (`Succeeded`), the underlying storage provider automatically clears `checkpoint_json = NULL` to avoid unnecessary database bloat.
- **Low I/O Overhead:** Checkpoint payloads are persisted to storage alongside progress updates without polluting list views or dashboard SSE feeds.

---

## When to Use IJobContext

**Use it when:**
- You need the job ID for logging or external correlation
- You want to report progress for long-running jobs
- You need to know if this is a retry (`Attempt > 1`)
- You need access to tags or queue name inside the job

**Don't use it when:**
- You only need your input data — that comes through `IJob<T>.ExecuteAsync(TInput, ...)`
- You need storage access — inject your storage service directly

---

## Using the Context From Other Services

`IJobContext` is registered as a **scoped** service, and every job runs in its own scope. So any service that is resolved inside that scope (for example an application service injected into your job) can take `IJobContext` in its constructor:

```csharp
public sealed class AuditTrail(IJobContext context)
{
    public string CorrelationId => context.JobId.Value.ToString();
}
```

- Resolving `IJobContext` outside a job execution (from a web request, a singleton, a hosted service) throws an `InvalidOperationException` ("IJobContext is only available during job execution").
- There is no public "optional accessor". If a service must work both inside and outside jobs, pass the values it needs explicitly, or read them from the structured logging scope NexJob opens around every execution (`NexJob.JobId`, `NexJob.JobType`, `NexJob.Queue`, `NexJob.Attempt`, `NexJob.TraceParent`; see [OpenTelemetry](12-OpenTelemetry.md)).

---

## Next Steps

- [Dashboard](10-Dashboard.md) — See progress updates in the UI
- [Best Practices](13-Best-Practices.md) — When to report progress
- [Writing Tests](14-Writing-Tests.md) — Testing jobs with IJobContext
