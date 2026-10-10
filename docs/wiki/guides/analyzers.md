# Roslyn Diagnostic Analyzers

NexJob ships with built-in compile-time Roslyn Diagnostic Analyzers bundled directly in the core `NexJob` package (`analyzers/dotnet/cs/NexJob.Analyzers.dll`). Installing `NexJob` automatically activates real-time guardrails in your IDE (Visual Studio, JetBrains Rider, VS Code) without requiring any extra package installation or setup.

The analyzers act as guardrails against common background job authoring bugs that the C# compiler cannot catch on its own.

---

## Configuration & Severity

All NexJob analyzer rules default to **`Info`** severity (and specific rules such as `NXJ011` and `NXJ016` are opt-in, disabled by default).

Because default severity is `Info`:
- Diagnostics appear as suggestions in IDEs.
- Builds in the terminal or CI under `TreatWarningsAsErrors = true` do **not** fail by default.

### Configuring in `.editorconfig`

To enforce rules as warnings/errors or enable opt-in rules, add configuration to your solution or project's `.editorconfig`:

```ini
[*.cs]
# Elevate all NexJob reliability or design rules to warning (breaks build under TreatWarningsAsErrors in CI/terminal)
dotnet_analyzer_diagnostic.category-Reliability.severity = warning
dotnet_analyzer_diagnostic.category-Design.severity = warning

# Elevate an individual rule
dotnet_diagnostic.NXJ001.severity = warning

# Enable opt-in rules (disabled by default)
dotnet_diagnostic.NXJ011.severity = info
dotnet_diagnostic.NXJ016.severity = info

# Disable a specific rule
dotnet_diagnostic.NXJ005.severity = none
```

---

## Rules Index

| Rule ID | Category | Default Severity | Short Description |
|---|---|---|---|
| [NXJ001](#nxj001) | Reliability | Info | Avoid blocking calls in job execution |
| [NXJ002](#nxj002) | Reliability | Info | Avoid `DateTime.Now` in job execution |
| [NXJ003](#nxj003) | Design | Info | Job class must be public and instantiable |
| [NXJ004](#nxj004) | Reliability | Info | Propagate `CancellationToken` in job execution |
| [NXJ005](#nxj005) | Reliability | Info | Avoid static mutable state in job classes |
| [NXJ006](#nxj006) | Reliability | Info | Avoid fire-and-forget tasks in job execution |
| [NXJ007](#nxj007) | Design | Info | Avoid direct service instantiation inside job |
| [NXJ010](#nxj010) | Reliability | Info | Enqueue with `deadlineAfter` should specify bounded `maxAttempts` |
| [NXJ011](#nxj011) | Design | Info (Opt-in) | Consider providing an `idempotencyKey` for background jobs |
| [NXJ015](#nxj015) | Reliability | Info | Prevent recursive job continuation |
| [NXJ016](#nxj016) | Reliability | Info (Opt-in) | Enqueue without explicit queue may cause cross-service prefix mismatch |
| [NXJ017](#nxj017) | Reliability | Info | Avoid swallowing exceptions in job execution |
| [NXJ018](#nxj018) | Reliability | Info | Avoid swallowing `OperationCanceledException` in job execution |
| [NXJ019](#nxj019) | Reliability | Info | Invalid job attribute argument |

---

## Rule Details

### NXJ001

**Title:** Avoid blocking calls in job execution  
**Category:** Reliability  
**Default Severity:** Info  

Background jobs execute inside worker threads managed by the dispatcher. Calling synchronous blocking APIs such as `.Result`, `.Wait()`, `Thread.Sleep()`, `GetAwaiter().GetResult()`, `Task.WaitAll(...)`, or `Task.WaitAny(...)` blocks the executing thread, starving the .NET thread pool and preventing other jobs from progressing.

#### Example

=== "Wrong"
    ```csharp
    public sealed class ExportReportJob : IJob
    {
        public Task ExecuteAsync(CancellationToken ct)
        {
            // Blocking call starves worker thread pool
            var data = FetchDataAsync().GetAwaiter().GetResult();
            Thread.Sleep(1000);
            return Task.CompletedTask;
        }
    }
    ```

=== "Correct"
    ```csharp
    public sealed class ExportReportJob : IJob
    {
        public async Task ExecuteAsync(CancellationToken ct)
        {
            var data = await FetchDataAsync(ct);
            await Task.Delay(1000, ct);
        }
    }
    ```

---

### NXJ002

**Title:** Avoid `DateTime.Now` in job execution  
**Category:** Reliability  
**Default Severity:** Info  

`DateTime.Now` and `DateTime.Today` depend on local machine time zones and daylight saving time (DST) transitions. In distributed environments where workers run across multiple containers or servers in different regions, using local time produces inconsistent timestamps, corrupted audit histories, and race conditions. Use `DateTime.UtcNow`.

#### Example

=== "Wrong"
    ```csharp
    public sealed class BillingJob : IJob
    {
        public async Task ExecuteAsync(CancellationToken ct)
        {
            // Depends on local server timezone
            var today = DateTime.Today;
            await ProcessBillingAsync(today, ct);
        }
    }
    ```

=== "Correct"
    ```csharp
    public sealed class BillingJob : IJob
    {
        public async Task ExecuteAsync(CancellationToken ct)
        {
            var nowUtc = DateTime.UtcNow;
            await ProcessBillingAsync(nowUtc, ct);
        }
    }
    ```

---

### NXJ003

**Title:** Job class must be public and instantiable  
**Category:** Design  
**Default Severity:** Info  

NexJob instantiates jobs dynamically using dependency injection when worker slots pick them up. If a job class or record is `internal`, `abstract`, or lacks a public constructor, dependency injection fails at runtime when the job is dequeued.

#### Example

=== "Wrong"
    ```csharp
    // Internal or abstract job cannot be resolved by DI at runtime
    internal abstract class ProcessOrderJob : IJob
    {
        public abstract Task ExecuteAsync(CancellationToken ct);
    }
    ```

=== "Correct"
    ```csharp
    public sealed class ProcessOrderJob : IJob
    {
        private readonly IOrderService _orderService;

        public ProcessOrderJob(IOrderService orderService)
        {
            _orderService = orderService;
        }

        public async Task ExecuteAsync(CancellationToken ct)
        {
            await _orderService.ProcessAsync(ct);
        }
    }
    ```

---

### NXJ004

**Title:** Propagate `CancellationToken` in job execution  
**Category:** Reliability  
**Default Severity:** Info  

NexJob signals cancellation when a job reaches its deadline or when the host undergoes graceful shutdown. Omitting the available `CancellationToken` in async calls or passing `CancellationToken.None` prevents the runtime from terminating long-running operations promptly, causing jobs to hang during shutdown and requiring orphan recovery.

#### Example

=== "Wrong"
    ```csharp
    public sealed class SyncDataJob : IJob
    {
        public async Task ExecuteAsync(CancellationToken ct)
        {
            // ct is ignored; cannot be cancelled on graceful shutdown or deadline
            await _client.DownloadUpdatesAsync();
            await Task.Delay(2000);
        }
    }
    ```

=== "Correct"
    ```csharp
    public sealed class SyncDataJob : IJob
    {
        public async Task ExecuteAsync(CancellationToken ct)
        {
            await _client.DownloadUpdatesAsync(ct);
            await Task.Delay(2000, ct);
        }
    }
    ```

---

### NXJ005

**Title:** Avoid static mutable state in job classes  
**Category:** Reliability  
**Default Severity:** Info  

Worker nodes run multiple job instances concurrently in parallel threads and processes. Static mutable fields, collections (e.g. `List<T>`, `Dictionary<K,V>`), and static auto-properties are shared across all executions, causing data corruption and thread-safety bugs. Job state should be passed via parameters, stored in instance fields resolved via scoped DI, or persisted in storage.

#### Example

=== "Wrong"
    ```csharp
    public sealed class ImportJob : IJob
    {
        // Shared across concurrent executions - race condition!
        private static int _processedCount;
        private static readonly List<string> _errors = new();

        public async Task ExecuteAsync(CancellationToken ct)
        {
            _processedCount++;
            _errors.Add("sample");
            await Task.Yield();
        }
    }
    ```

=== "Correct"
    ```csharp
    public sealed class ImportJob : IJob
    {
        private readonly IMetricsCollector _metrics;

        public ImportJob(IMetricsCollector metrics)
        {
            _metrics = metrics;
        }

        public async Task ExecuteAsync(CancellationToken ct)
        {
            await _metrics.IncrementProcessedAsync(ct);
        }
    }
    ```

---

### NXJ006

**Title:** Avoid fire-and-forget tasks in job execution  
**Category:** Reliability  
**Default Severity:** Info  

Background jobs must be fully awaited. Invoking `Task.Run(...)` or unawaited async operations without awaiting them causes the task to escape NexJob's execution pipeline. If an unawaited task throws an exception or takes longer to complete, the job may already be marked as succeeded, and errors will crash unobserved or escape retries and dead-letter handling.

#### Example

=== "Wrong"
    ```csharp
    public sealed class NotificationJob : IJob
    {
        public Task ExecuteAsync(CancellationToken ct)
        {
            // Fire-and-forget task escapes dispatcher monitoring and error handling
            _ = Task.Run(() => SendEmailAsync(), ct);
            return Task.CompletedTask;
        }
    }
    ```

=== "Correct"
    ```csharp
    public sealed class NotificationJob : IJob
    {
        public async Task ExecuteAsync(CancellationToken ct)
        {
            await SendEmailAsync(ct);
        }
    }
    ```

---

### NXJ007

**Title:** Avoid direct service instantiation inside job  
**Category:** Design  
**Default Severity:** Info  

Jobs execute inside a dedicated scoped dependency injection container. Instantiating services directly with `new MyDbContext()` or `new HttpClient()` bypasses lifetime management, connection pooling, and disposal. Inject required dependencies through the job's constructor instead.

#### Example

=== "Wrong"
    ```csharp
    public sealed class CleanUpJob : IJob
    {
        public async Task ExecuteAsync(CancellationToken ct)
        {
            // Bypasses DI scope, lifetime management, and connection pooling
            using var db = new AppDbContext();
            await db.PurgeOldRecordsAsync(ct);
        }
    }
    ```

=== "Correct"
    ```csharp
    public sealed class CleanUpJob : IJob
    {
        private readonly AppDbContext _db;

        public CleanUpJob(AppDbContext db)
        {
            _db = db;
        }

        public async Task ExecuteAsync(CancellationToken ct)
        {
            await _db.PurgeOldRecordsAsync(ct);
        }
    }
    ```

---

### NXJ010

**Title:** Enqueue with `deadlineAfter` should specify bounded `maxAttempts`  
**Category:** Reliability  
**Default Severity:** Info  

When setting `deadlineAfter`, a failing job without an explicit `maxAttempts` limit may retry continuously in tight succession until the deadline expires. Providing an explicit `maxAttempts` bounds the total number of retries, saving resources and surfacing persistent failures to the dead-letter queue faster.

#### Example

=== "Wrong"
    ```csharp
    // Retries indefinitely until the 1-hour deadline runs out
    await scheduler.EnqueueAsync<SyncJob>(
        deadlineAfter: TimeSpan.FromHours(1),
        cancellationToken: cancellationToken);
    ```

=== "Correct"
    ```csharp
    // Bounded to 3 attempts within the 1-hour window
    await scheduler.EnqueueAsync<SyncJob>(
        maxAttempts: 3,
        deadlineAfter: TimeSpan.FromHours(1),
        cancellationToken: cancellationToken);
    ```

---

### NXJ011

**Title:** Consider providing an `idempotencyKey` for background jobs  
**Category:** Design  
**Default Severity:** Info (Opt-in)  

In distributed systems, message triggers, network retries, and producer replays can lead to the same job being scheduled more than once. Providing an `idempotencyKey` (such as a broker message ID or business transaction ID) enables NexJob to deduplicate requests automatically.

!!! note
    NXJ011 is disabled by default to avoid noise in codebases where not every job requires deduplication. Enable it via `.editorconfig` when desired.

#### Example

=== "Wrong"
    ```csharp
    // Can enqueue duplicate records if the HTTP request or message is retried
    await scheduler.EnqueueAsync<ChargeCustomerJob, ChargePayload>(
        input,
        cancellationToken: cancellationToken);
    ```

=== "Correct"
    ```csharp
    await scheduler.EnqueueAsync<ChargeCustomerJob, ChargePayload>(
        input,
        idempotencyKey: $"payment-{input.PaymentId}",
        cancellationToken: cancellationToken);
    ```

---

### NXJ015

**Title:** Prevent recursive job continuation  
**Category:** Reliability  
**Default Severity:** Info  

Directly scheduling the same job type from inside its own `ExecuteAsync` method without termination conditions creates infinite continuation loops that flood queues and bypass scheduling cadence controls. For repeating tasks, prefer [Recurring Jobs](../concepts/recurring-jobs.md).

#### Example

=== "Wrong"
    ```csharp
    public sealed class PollingJob : IJob
    {
        private readonly IScheduler _scheduler;

        public PollingJob(IScheduler scheduler) => _scheduler = scheduler;

        public async Task ExecuteAsync(CancellationToken ct)
        {
            await PollAsync(ct);
            // Infinite recursive loop
            await _scheduler.EnqueueAsync<PollingJob>(cancellationToken: ct);
        }
    }
    ```

=== "Correct"
    ```csharp
    // Register as a recurring job instead:
    builder.Services.AddNexJob(o =>
    {
        o.AddRecurringJob<PollingJob>("polling-job", CronExpressions.EveryMinute);
    });
    ```

---

### NXJ016

**Title:** Enqueue without explicit queue may cause cross-service prefix mismatch  
**Category:** Reliability  
**Default Severity:** Info (Opt-in)  

In multi-service setups where different services share the same database with distinct `QueuePrefix` configurations, enqueuing without an explicit `queue` parameter places the job in the producer's default queue. Consumer worker services polling a different prefixed queue will never pick it up.

!!! note
    NXJ016 is disabled by default. Enable it in multi-service distributed architectures via `.editorconfig`.

#### Example

=== "Wrong"
    ```csharp
    // Omitting queue targets producer's default queue, consumer may not listen to it
    await scheduler.EnqueueAsync<ExternalProcessingJob, JobPayload>(
        input,
        cancellationToken: cancellationToken);
    ```

=== "Correct"
    ```csharp
    await scheduler.EnqueueAsync<ExternalProcessingJob, JobPayload>(
        input,
        queue: "processing-service",
        cancellationToken: cancellationToken);
    ```

---

### NXJ017

**Title:** Avoid swallowing exceptions in job execution  
**Category:** Reliability  
**Default Severity:** Info  

Catching general exceptions (`catch (Exception)` or untyped `catch`) inside `IJob.ExecuteAsync` without rethrowing them causes the job to finish as succeeded in NexJob storage. When this happens:
- The job is marked as `Succeeded`.
- Configured retry policies never trigger.
- The failure never reaches dead-letter handlers or alerts.

If you need to log or perform recovery, log the error and rethrow, or record partial progress before propagating.

#### Legitimate Exceptions
In batch processing jobs handling collections of independent records in a loop, catching individual item errors, logging them, and continuing the loop may be intentional. In such cases, suppress the diagnostic locally or configure `.editorconfig`.

#### Example

=== "Wrong"
    ```csharp
    public sealed class ProcessPaymentJob : IJob
    {
        public async Task ExecuteAsync(CancellationToken ct)
        {
            try
            {
                await ChargeCardAsync(ct);
            }
            catch (Exception ex)
            {
                // Swallowed! Job finishes as 'Succeeded'; retry and dead-letter are skipped.
                _logger.LogError(ex, "Failed to charge card");
            }
        }
    }
    ```

=== "Correct"
    ```csharp
    public sealed class ProcessPaymentJob : IJob
    {
        public async Task ExecuteAsync(CancellationToken ct)
        {
            try
            {
                await ChargeCardAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to charge card; will retry");
                throw; // Rethrow to trigger NexJob retry & dead-letter pipeline
            }
        }
    }
    ```

---

### NXJ018

**Title:** Avoid swallowing `OperationCanceledException` in job execution  
**Category:** Reliability  
**Default Severity:** Info  

Catching `OperationCanceledException` or `TaskCanceledException` without rethrowing causes graceful host shutdowns to appear as normal completions. NexJob's dispatcher detects unhandled cancellation during shutdown and automatically requeues the job without consuming an attempt. If cancellation is swallowed, the job is marked as `Succeeded` even though it was aborted prematurely.

#### Legitimate Exceptions
If your job uses an internal per-call timeout via a linked `CancellationTokenSource` (e.g. `cts.CancelAfter(5000)` for a single remote call) and intentionally catches that timeout to fall back to an alternate path, catching `OperationCanceledException` for that specific token is legitimate. You can suppress the rule on that method or block.

#### Example

=== "Wrong"
    ```csharp
    public sealed class HeavyReportJob : IJob
    {
        public async Task ExecuteAsync(CancellationToken ct)
        {
            try
            {
                await GenerateReportAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // Swallowed! Host shutdown aborts early but job is marked Succeeded without requeue.
                _logger.LogWarning("Report cancelled");
            }
        }
    }
    ```

=== "Correct"
    ```csharp
    public sealed class HeavyReportJob : IJob
    {
        public async Task ExecuteAsync(CancellationToken ct)
        {
            try
            {
                await GenerateReportAsync(ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Shutdown or timeout signalled; propagating for requeue");
                throw; // Propagate so NexJob can requeue the job
            }
        }
    }
    ```

---

### NXJ019

**Title:** Invalid job attribute argument  
**Category:** Reliability  
**Default Severity:** Info  

NexJob validates job attributes lazily at runtime when the job is first executed by a worker. Supplying invalid compile-time constants (such as negative retry attempts, non-positive concurrency limits, or invalid timeout strings) causes runtime `ArgumentOutOfRangeException` or `FormatException`. NXJ019 catches these configuration errors directly at compile time.

Validated attributes include:
- `[Retry(attempts)]`: `attempts` must be `>= 0`.
- `[Throttle(maxConcurrent, resource)]`: `maxConcurrent` must be `>= 1` and `resource` must not be null or whitespace.
- `[ExecutionTimeout(timeSpanString)]`: must be a valid, positive `TimeSpan`.

#### Example

=== "Wrong"
    ```csharp
    // Invalid: negative retry attempts, zero concurrency, negative timeout
    [Retry(-1)]
    [Throttle(0, "api")]
    [ExecutionTimeout("-00:10:00")]
    public sealed class OrderJob : IJob
    {
        public Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
    }
    ```

=== "Correct"
    ```csharp
    [Retry(3)]
    [Throttle(5, "api")]
    [ExecutionTimeout("00:10:00")]
    public sealed class OrderJob : IJob
    {
        public Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
    }
    ```
