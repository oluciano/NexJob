# Common Scenarios

Real-world use cases with minimal working code.

---

## Send Email After Order

Enqueue an email job after completing an order. The email job runs independently, not blocking the HTTP response.

```csharp
// HTTP endpoint
app.MapPost("/orders", async (OrderInput input, IScheduler scheduler, IDb db, HttpContext ctx) =>
{
    var order = await db.Orders.CreateAsync(input, ct: ctx.RequestAborted);

    // Email fires off independently
    await scheduler.EnqueueAsync<OrderConfirmationJob, OrderConfirmationInput>(
        new OrderConfirmationInput(order.Id, order.Email),
        queue: "emails",
        cancellationToken: ctx.RequestAborted);

    return Results.Created($"/orders/{order.Id}", order);
});

// Job
public sealed class OrderConfirmationJob : IJob<OrderConfirmationInput>
{
    private readonly IEmailService _email;

    public OrderConfirmationJob(IEmailService email) => _email = email;

    public async Task ExecuteAsync(OrderConfirmationInput input, CancellationToken ct)
    {
        await _email.SendAsync(input.Email, "Order Confirmed", $"Order {input.OrderId} is confirmed.", ct);
    }
}

public sealed record OrderConfirmationInput(Guid OrderId, string Email);
```

---

## Process Upload Async

A user uploads a file. Processing takes minutes. Enqueue a job and return immediately.

```csharp
app.MapPost("/imports", async (IFormFile file, IScheduler scheduler, IHttpContextAccessor http, IDb db) =>
{
    // Save file to temp storage
    var path = await SaveTempAsync(file);

    // Create import record
    var import = await db.Imports.CreateAsync(path, status: "pending");

    // Enqueue processing job
    await scheduler.EnqueueAsync<ImportProcessorJob, ImportInput>(
        new ImportInput(import.Id, path),
        queue: "imports",
        idempotencyKey: $"import-{import.Id}",
        cancellationToken: CancellationToken.None);

    return Results.Accepted($"/imports/{import.Id}", new { ImportId = import.Id });
});

public sealed class ImportProcessorJob : IJob<ImportInput>
{
    private readonly IDb _db;
    private readonly IImportService _import;

    public ImportProcessorJob(IDb db, IImportService import)
    {
        _db = db;
        _import = import;
    }

    public async Task ExecuteAsync(ImportInput input, CancellationToken ct)
    {
        await _db.Imports.UpdateStatusAsync(input.ImportId, "processing", ct);

        try
        {
            await _import.ProcessAsync(input.FilePath, ct);
            await _db.Imports.UpdateStatusAsync(input.ImportId, "completed", ct);
        }
        catch (Exception ex)
        {
            await _db.Imports.UpdateStatusAsync(input.ImportId, $"failed: {ex.Message}", ct);
            throw; // Let retry/dead-letter handle it
        }
    }
}

public sealed record ImportInput(Guid ImportId, string FilePath);
```

---

## Retry External API

External APIs fail. Use retries with exponential backoff to handle transient errors.

```csharp
[Retry(5, InitialDelay = "00:00:10", Multiplier = 2.0, MaxDelay = "00:05:00")]
public sealed class WebhookDeliveryJob : IJob<WebhookInput>
{
    private readonly HttpClient _http;

    public WebhookDeliveryJob(HttpClient http) => _http = http;

    public async Task ExecuteAsync(WebhookInput input, CancellationToken ct)
    {
        var response = await _http.PostAsJsonAsync(input.Url, input.Payload, ct);
        response.EnsureSuccessStatusCode(); // Throws on 4xx/5xx — triggers retry
    }
}

public sealed record WebhookInput(string Url, object Payload);
```

**Behavior:**
- Attempt 1: fails → wait 10s
- Attempt 2: fails → wait 20s
- Attempt 3: fails → wait 40s
- Attempt 4: fails → wait 80s
- Attempt 5: fails → dead-letter

---

## Recurring Cleanup Job

Run a cleanup job daily at 2 AM.

```csharp
builder.Services.AddNexJob(options =>
{
    options.AddRecurringJob<CleanupOldLogsJob>(
        recurringJobId: "cleanup-daily",
        cron: "0 2 * * *");
});

public sealed class CleanupOldLogsJob : IJob
{
    private readonly IDbContext _db;

    public CleanupOldLogsJob(IDbContext db) => _db = db;

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        var deleted = await _db.ExecutionLogs
            .Where(l => l.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        Console.WriteLine($"Deleted {deleted} old log entries");
    }
}
```

---

## Prevent Duplicate Execution

An order payment webhook may fire twice. Use idempotency to prevent duplicate processing.

```csharp
// Webhook endpoint — may be called twice by the payment provider
app.MapPost("/webhooks/payment", async (PaymentEvent evt, IScheduler scheduler) =>
{
    await scheduler.EnqueueAsync<ProcessPaymentJob, PaymentInput>(
        new PaymentInput(evt.OrderId, evt.Amount),
        idempotencyKey: $"payment-{evt.OrderId}",
        duplicatePolicy: DuplicatePolicy.RejectAlways,
        cancellationToken: CancellationToken.None);

    return Results.Ok();
});

public sealed class ProcessPaymentJob : IJob<PaymentInput>
{
    private readonly IPaymentProcessor _processor;

    public ProcessPaymentJob(IPaymentProcessor processor) => _processor = processor;

    public async Task ExecuteAsync(PaymentInput input, CancellationToken ct)
    {
        // This only runs once per order, even if the webhook fires twice
        await _processor.ProcessAsync(input.OrderId, input.Amount, ct);
    }
}

public sealed record PaymentInput(Guid OrderId, decimal Amount);
```

See [Idempotency](17-Idempotency.md) for all duplicate policies.

---

## Next Steps

- [Idempotency](17-Idempotency.md) — Deep dive on duplicate prevention
- [Retry & Dead Letter](06-Retry-And-Dead-Letter.md) — Configure retry policies
- [Troubleshooting](16-Troubleshooting.md) — Debug common issues
