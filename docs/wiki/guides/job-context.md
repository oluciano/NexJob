---
title: "Access Runtime Job Metadata with IJobContext in NexJob"
sidebarTitle: "Job Context"
description: "Use IJobContext in NexJob jobs to access job ID, attempt number, queue name, tags, and report progress or save checkpoints from within a running job."
---

`IJobContext` gives your job access to runtime metadata about its own execution — the job ID, which attempt this is, the queue it came from, and the tags attached at enqueue time. You can also use it to report progress to the dashboard and persist checkpoints so long-running jobs can resume after a worker restart. Inject it through the constructor like any other scoped service.

## Injecting `IJobContext`

NexJob registers `IJobContext` as a scoped service tied to each job execution. Declare it in your job's constructor and NexJob resolves it automatically.

```csharp
public sealed class LongRunningJob : IJob
{
    private readonly IJobContext _context;

    public LongRunningJob(IJobContext context) => _context = context;

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var jobId   = _context.JobId;       // unique identifier for this execution
        var attempt = _context.Attempt;     // 1 on first run, 2 on first retry, etc.
        var queue   = _context.Queue;       // the queue this job was fetched from
        var tags    = _context.Tags;        // tags attached at enqueue time

        // your work here
    }
}
```

## Available Properties

| Property | Type | Description |
|---|---|---|
| `JobId` | `JobId` | Unique identifier for this job execution. Useful for logging and external correlation. |
| `Attempt` | `int` | Current attempt number, 1-based. First execution is `1`, first retry is `2`. |
| `MaxAttempts` | `int` | Maximum number of attempts configured for this job. |
| `Queue` | `string` | The name of the queue this job was fetched from. |
| `RecurringJobId` | `string?` | The recurring job definition ID, or `null` if the job was enqueued directly. |
| `Tags` | `IReadOnlyList<string>` | Tags attached to this job at enqueue time. |

## Reporting Progress

Call `ReportProgressAsync` to push a progress update to the storage layer. The dashboard reflects updates in real time via SSE.

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

### Progress Extension Methods

NexJob ships convenience extensions that wire up progress reporting to collection iteration automatically.


=== "IAsyncEnumerable"

    ```csharp
    await foreach (var item in source.WithProgress(_context, ct))
    {
        await ProcessAsync(item, ct);
    }
    ```

    Reports the percentage of items already yielded. The extension reads the whole source into memory first to determine the total count, so use it only for collections that fit comfortably in memory.

=== "IEnumerable"

    ```csharp
    foreach (var item in items.WithProgress(_context))
    {
        await ProcessAsync(item, ct);
    }
    ```

    Fire-and-forget progress for synchronous collections. The sequence is materialised first to know its size, and the progress call is not awaited — storage errors while reporting are not surfaced to the job.




## Progress Checkpoints for Long-Running Jobs

For batch processing, data migrations, and multi-step workflows, use checkpoints to persist exactly how far the job has progressed. If the job is interrupted — by a worker restart, a transient crash, or a retry after failure — it resumes from the last checkpoint rather than starting over.

### Saving a Checkpoint

Call `SaveCheckpointAsync<TState>` with your state object. NexJob serialises it and persists it atomically alongside the progress update.

```csharp
public sealed class LargeDataSyncJob : IJob
{
    private readonly IJobContext _context;
    private readonly IRecordReader _reader;
    private readonly IRecordProcessor _processor;

    public LargeDataSyncJob(
        IJobContext context,
        IRecordReader reader,
        IRecordProcessor processor)
    {
        _context = context;
        _reader = reader;
        _processor = processor;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        // Resume from previous checkpoint, or start fresh on first attempt
        var state = _context.GetCheckpoint<SyncCheckpoint>()
                    ?? new SyncCheckpoint(LastProcessedId: 0, TotalBatches: 0);

        var batch = await _reader.FetchNextBatchAsync(afterId: state.LastProcessedId, ct);

        while (batch.Count > 0)
        {
            await _processor.ProcessBatchAsync(batch, ct);

            state = state with
            {
                LastProcessedId = batch.Last().Id,
                TotalBatches = state.TotalBatches + 1
            };

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

### Resuming from a Checkpoint

Call `GetCheckpoint<TState>()` at the start of `ExecuteAsync`. It returns `null` on the first attempt (no checkpoint saved yet) and the deserialised state object on subsequent attempts.

```csharp
var state = _context.GetCheckpoint<SyncCheckpoint>()
            ?? new SyncCheckpoint(LastProcessedId: 0, TotalBatches: 0);
```

### Checkpoint Lifecycle

<div class="grid cards" markdown>
  -   **Persists across retries**

    If the job fails, `CheckpointJson` is preserved in storage. The next attempt calls `GetCheckpoint` and picks up exactly where execution stopped.

  -   **Auto-clears on success**

    When the job completes successfully, NexJob automatically clears the saved checkpoint to avoid unnecessary storage bloat.

  -   **Low I/O overhead**

    Checkpoint payloads are persisted alongside progress updates in a single database operation and do not appear in list views or SSE dashboard feeds.

</div>

## Detecting Retries

Use `Attempt` to branch logic on retries — for example to skip expensive setup steps you already completed:

```csharp
public async Task ExecuteAsync(CancellationToken ct)
{
    if (_context.Attempt == 1)
    {
        await _setup.PrepareAsync(ct);  // only needed on first attempt
    }

    await _work.RunAsync(ct);
}
```

!!! note
    Jobs are transient services, so each attempt creates a new instance. Do not track attempt state in instance fields — use `IJobContext.Attempt` or a checkpoint instead.


## Using `IJobContext` from Other Services

Because `IJobContext` is scoped, any service resolved inside the same job scope can inject it directly:

```csharp
public sealed class AuditTrail(IJobContext context)
{
    public string CorrelationId => context.JobId.Value.ToString();
}
```

!!! warning
    Resolving `IJobContext` outside a job execution — from a web request, a singleton, or a hosted service — throws `InvalidOperationException: "IJobContext is only available during job execution"`. If a service must work both inside and outside jobs, pass the values it needs explicitly, or read them from the structured logging scope NexJob opens around every execution (`NexJob.JobId`, `NexJob.JobType`, `NexJob.Queue`, `NexJob.Attempt`).


## When to Use `IJobContext`

<div class="grid cards" markdown>
  -   **Use it when you need to**

    Access the job ID for logging or external correlation, report progress for long-running jobs, detect retries (`Attempt > 1`), or read the queue name or tags from inside the job.

  -   **Skip it when you only need**

    Your input data — that comes through `IJob<T>.ExecuteAsync(TInput, ...)`. Storage access — inject your storage service directly.

</div>
