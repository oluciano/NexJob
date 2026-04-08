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

Automatically calculates percentage based on source position. The source must implement `IAsyncEnumerable<T>` with known count.

### IEnumerable with Progress

```csharp
foreach (var item in items.WithProgress(_context))
{
    await ProcessAsync(item, ct);
}
```

Fire-and-forget progress for synchronous collections.

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

## Next Steps

- [Dashboard](10-Dashboard.md) — See progress updates in the UI
- [Best Practices](13-Best-Practices.md) — When to report progress
- [Writing Tests](14-Writing-Tests.md) — Testing jobs with IJobContext
