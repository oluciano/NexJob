---
title: "Common NexJob Background Job Patterns and Scenarios"
sidebarTitle: "Common Scenarios"
description: "Real-world NexJob patterns: email sending, report generation, webhook delivery, order processing, and scheduled cleanup with working code examples."
---

The following scenarios cover the most common patterns you will encounter when integrating NexJob into a production application. Each example is self-contained and shows the minimal code needed to solve the problem, along with notes on the key design decisions.

???+ "Send a confirmation email after an order"
    **Problem:** Your HTTP endpoint creates an order and needs to send a confirmation email, but you don't want the email delivery to block the HTTP response or cause the request to fail if the email service is temporarily unavailable.

    **Solution:** Enqueue the email job after persisting the order and return immediately. The job runs asynchronously on a worker.

    ```csharp
    // HTTP endpoint
    app.MapPost("/orders", async (OrderInput input, IScheduler scheduler, IDb db, HttpContext ctx) =>
    {
        var order = await db.Orders.CreateAsync(input, ct: ctx.RequestAborted);

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
            await _email.SendAsync(
                input.Email,
                "Order Confirmed",
                $"Order {input.OrderId} is confirmed.",
                ct);
        }
    }

    public sealed record OrderConfirmationInput(Guid OrderId, string Email);
    ```

    **Notes:**
    - Route email jobs to a dedicated `"emails"` queue so a spike in email volume does not slow down other workloads.
    - Add `[Throttle("email-service", maxConcurrent: 10)]` to the job class if your email provider enforces concurrency limits.


???+ "Process a large file upload asynchronously"
    **Problem:** A user uploads a file that takes minutes to process. You must respond to the HTTP request immediately and process the file in the background.

    **Solution:** Save the file to temporary storage, create an import record, enqueue the processor job, and return a `202 Accepted` with the import ID the client can poll.

    ```csharp
    app.MapPost("/imports", async (IFormFile file, IScheduler scheduler, IDb db) =>
    {
        var path = await SaveTempAsync(file);
        var import = await db.Imports.CreateAsync(path, status: "pending");

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
                throw; // let NexJob handle retries and dead-lettering
            }
        }
    }

    public sealed record ImportInput(Guid ImportId, string FilePath);
    ```

    **Notes:**
    - The `idempotencyKey` prevents duplicate jobs if the client retries the upload request.
    - For very large files, inject `IJobContext` and call `ReportProgressAsync` inside the processing loop so the dashboard shows live progress.


???+ "Deliver a webhook with exponential backoff"
    **Problem:** You need to deliver a webhook payload to a customer-supplied URL. Delivery may fail due to transient network errors or the customer's server being temporarily down.

    **Solution:** Use `[Retry]` with exponential backoff and let NexJob handle rescheduling automatically on each failure.

    ```csharp
    [Retry(5, InitialDelay = "00:00:10", Multiplier = 2.0, MaxDelay = "00:05:00")]
    public sealed class WebhookDeliveryJob : IJob<WebhookInput>
    {
        private readonly HttpClient _http;

        public WebhookDeliveryJob(HttpClient http) => _http = http;

        public async Task ExecuteAsync(WebhookInput input, CancellationToken ct)
        {
            var response = await _http.PostAsJsonAsync(input.Url, input.Payload, ct);
            response.EnsureSuccessStatusCode(); // throws on 4xx/5xx — triggers retry
        }
    }

    public sealed record WebhookInput(string Url, object Payload);
    ```

    **Retry schedule (with ±10% jitter applied to each delay):**

    | Attempt | Delay before next attempt |
    |---|---|
    | 1 | 10 s |
    | 2 | 20 s |
    | 3 | 40 s |
    | 4 | 80 s |
    | 5 | Dead-lettered |

    **Notes:**
    - Add a `[Throttle("webhook-sender", maxConcurrent: 20)]` attribute to cap outbound concurrency across all webhook jobs.
    - Register an `IDeadLetterHandler<WebhookDeliveryJob>` to notify your team or mark the subscription as suspended when all retries are exhausted.


???+ "Scheduled daily cleanup with a recurring job"
    **Problem:** You need to delete log entries older than 30 days every night at 2 AM without any manual trigger.

    **Solution:** Register a recurring job using a cron expression.

    ```csharp
    builder.Services.AddNexJob(options =>
    {
        options.AddRecurringJob<CleanupOldLogsJob>(
            id: "cleanup-daily",
            cron: "0 2 * * *");  // runs at 02:00 UTC every day
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

    **Notes:**
    - In a multi-node deployment, NexJob uses a distributed lock to ensure the recurring job fires exactly once per scheduled interval — not once per node.
    - Route this job to a low-priority queue so it does not compete with user-facing work during off-peak processing hours.


???+ "Prevent duplicate payment processing with idempotency"
    **Problem:** A payment webhook from your provider may be delivered more than once. Charging the customer twice would be a serious bug.

    **Solution:** Pass an `idempotencyKey` when enqueuing the job and set `DuplicatePolicy.RejectAlways` so NexJob refuses to create a second job for the same order once the first has completed.

    ```csharp
    // Webhook endpoint — may be called more than once by the payment provider
    app.MapPost("/webhooks/payment", async (PaymentEvent evt, IScheduler scheduler) =>
    {
        try
        {
            await scheduler.EnqueueAsync<ProcessPaymentJob, PaymentInput>(
                new PaymentInput(evt.OrderId, evt.Amount),
                idempotencyKey: $"payment-{evt.OrderId}",
                duplicatePolicy: DuplicatePolicy.RejectAlways,
                cancellationToken: CancellationToken.None);
        }
        catch (DuplicateJobException)
        {
            // Already processed — acknowledge so the provider stops retrying
        }

        return Results.Ok();
    });

    public sealed class ProcessPaymentJob : IJob<PaymentInput>
    {
        private readonly IPaymentProcessor _processor;

        public ProcessPaymentJob(IPaymentProcessor processor) => _processor = processor;

        public async Task ExecuteAsync(PaymentInput input, CancellationToken ct)
        {
            await _processor.ProcessAsync(input.OrderId, input.Amount, ct);
        }
    }

    public sealed record PaymentInput(Guid OrderId, decimal Amount);
    ```

    **Notes:**
    - `DuplicatePolicy.RejectAlways` raises `DuplicateJobException` even while the first job is still active. Use `DuplicatePolicy.RejectIfActive` if you want to allow re-enqueueing after the first job completes.
    - Consider also adding an idempotency check inside `ProcessAsync` as a defence-in-depth measure.


???+ "Resume a long data migration after a restart"
    **Problem:** A multi-hour data migration from a legacy API can be interrupted by worker restarts or pod scaling events. Restarting from the beginning after each interruption wastes hours of work.

    **Solution:** Use `IJobContext.SaveCheckpointAsync` to persist progress after each batch, and `GetCheckpoint<TState>()` at startup to resume from the last saved position.

    ```csharp
    public sealed class DataMigrationJob : IJob<MigrationInput>
    {
        private readonly IJobContext _context;
        private readonly ILegacyApiClient _client;
        private readonly IDb _db;

        public DataMigrationJob(IJobContext context, ILegacyApiClient client, IDb db)
        {
            _context = context;
            _client = client;
            _db = db;
        }

        public async Task ExecuteAsync(MigrationInput input, CancellationToken ct)
        {
            // Resume from previous checkpoint, or start fresh
            var checkpoint = _context.GetCheckpoint<MigrationCheckpoint>()
                             ?? new MigrationCheckpoint(LastProcessedKey: null, RecordsMigrated: 0);

            var page = await _client.FetchRecordsAsync(
                input.BatchSize, checkpoint.LastProcessedKey, ct);

            while (page.HasItems)
            {
                await _db.BulkInsertAsync(page.Items, ct);

                checkpoint = new MigrationCheckpoint(
                    LastProcessedKey: page.LastKey,
                    RecordsMigrated: checkpoint.RecordsMigrated + page.Items.Count);

                await _context.SaveCheckpointAsync(
                    state: checkpoint,
                    percent: null,
                    message: $"Migrated {checkpoint.RecordsMigrated} records so far",
                    ct: ct);

                page = await _client.FetchRecordsAsync(
                    input.BatchSize, checkpoint.LastProcessedKey, ct);
            }
        }
    }

    public sealed record MigrationCheckpoint(string? LastProcessedKey, int RecordsMigrated);
    public sealed record MigrationInput(int BatchSize);
    ```

    **Notes:**
    - On success, NexJob automatically clears the saved checkpoint — no manual cleanup needed.
    - Combine with `[Throttle("legacy-api", maxConcurrent: 2)]` if the legacy system has concurrency restrictions.


???+ "High-throughput stream ingestion from Kafka or SQS"
    **Problem:** You are consuming a high-velocity event stream (Kafka, SQS, or a message bus) and need to process hundreds or thousands of events per second through NexJob.

    **Solution:** Configure batch acknowledgment and a short polling interval to maximise throughput, and attach the appropriate trigger integration.

    ```csharp
    var builder = WebApplication.CreateBuilder(args);

    // Choose your storage backend
    builder.Services.AddNexJobSqlServer(
        builder.Configuration.GetConnectionString("NexJobConnection")!);

    builder.Services.AddNexJob(options =>
    {
        options.Workers = 30;
        options.PollingInterval = TimeSpan.FromMilliseconds(20);

        // Cut database commit round-trips by 90 %+ with async batch acknowledgment
        options.EnableBatchAcknowledgment = true;
    })
    .AddKafkaTrigger(opt =>
    {
        opt.BootstrapServers = "kafka:9092";
        opt.Topic = "high-volume-events";
        opt.GroupId = "event-processing-group";
    });
    ```

    **Notes:**
    - Set `Workers` to match your processing capacity rather than defaulting to the minimum. Monitor p99 job duration and queue depth to tune the value.
    - Use the `[Retention(PurgeOnSuccess = true)]` attribute on high-frequency job classes to delete rows immediately on success and avoid table bloat.

