# Job Types

NexJob provides two job interfaces. Choose based on whether your job needs input data.

---

## IJob — No Input

Use when the job is self-contained and doesn't need external data.

```csharp
public sealed class CleanupOldLogsJob : IJob
{
    private readonly IDbContext _db;

    public CleanupOldLogsJob(IDbContext db) => _db = db;

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        await _db.Logs.Where(l => l.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
    }
}

// Enqueue
await scheduler.EnqueueAsync<CleanupOldLogsJob>(cancellationToken: ct);
```

**When to use:** The job knows what to do from its dependencies alone. Examples: cleanup, health checks, periodic syncs where the job queries for its own work.

---

## IJob<TInput> — Structured Input

Use when the job needs specific data to execute.

```csharp
public sealed class ProcessOrderJob : IJob<ProcessOrderInput>
{
    private readonly IOrderProcessor _processor;

    public ProcessOrderJob(IOrderProcessor processor) => _processor = processor;

    public async Task ExecuteAsync(ProcessOrderInput input, CancellationToken ct)
    {
        await _processor.ProcessAsync(input.OrderId, ct);
    }
}

public sealed record ProcessOrderInput(Guid OrderId);

// Enqueue with input
await scheduler.EnqueueAsync<ProcessOrderJob, ProcessOrderInput>(
    new ProcessOrderInput(orderId),
    cancellationToken: ct);
```

**When to use:** The job needs data determined at enqueue time. Examples: process a specific order, send email to a specific user, call a webhook with specific payload.

### Input Serialization

Input is serialized to JSON and stored in the `JobRecord`. Requirements:

- Input types must be JSON-serializable
- Use `record` types for immutability
- Keep input minimal — only what the job needs to execute

---

## Dead-Letter Handlers

When a job exhausts all retries, NexJob invokes its dead-letter handler. This is optional — jobs without handlers are simply marked as `Failed`.

```csharp
public sealed class PaymentDeadLetterHandler : IDeadLetterHandler<ProcessPaymentJob>
{
    private readonly IAlertService _alerts;

    public PaymentDeadLetterHandler(IAlertService alerts) => _alerts = alerts;

    public async Task HandleAsync(
        JobRecord failedJob,
        Exception lastException,
        CancellationToken cancellationToken)
    {
        await _alerts.SendAsync(
            $"Payment job {failedJob.Id} failed after {failedJob.Attempts} attempts: {lastException.Message}",
            cancellationToken);
    }
}

// Register
builder.Services.AddTransient<IDeadLetterHandler<ProcessPaymentJob>, PaymentDeadLetterHandler>();
```

**Key behaviors:**

- Handlers run in an isolated DI scope — exceptions are logged and swallowed, never crashing the dispatcher
- Works for both `IJob` and `IJob<T>` — use the job type as the generic parameter
- The `JobRecord` contains all execution context: attempts, error message, input, queue, tags

See [Retry & Dead Letter](06-Retry-And-Dead-Letter.md) for retry configuration.

---

## Auto-Registration

`AddNexJobJobs(assembly)` scans the assembly and registers all `IJob` and `IJob<T>` implementations as transient services. No manual registration needed.

```csharp
// Scans the assembly and registers:
// - CleanupOldLogsJob (IJob)
// - ProcessOrderJob (IJob<ProcessOrderInput>)
// - SendEmailJob (IJob<SendEmailInput>)
builder.Services.AddNexJobJobs(typeof(Program).Assembly);
```

**Rule:** Every public class implementing `IJob` or `IJob<T>` is registered. Internal job classes are ignored.

---

## Next Steps

- [Scheduling](03-Scheduling.md) — Enqueue, delay, schedule at specific time
- [Retry & Dead Letter](06-Retry-And-Dead-Letter.md) — Configure retries and dead-letter handlers
- [IJobContext](08-IJobContext.md) — Access runtime context inside jobs
