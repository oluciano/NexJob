# Minimal API Sample — 30-Second Demo

A bare-bones ASP.NET Core Minimal API demonstrating NexJob's core features: **enqueue**, **deadline**, and **dead-letter handling**.

## What This Demo Shows

✅ **Enqueue a job** — POST endpoint receives email, queues `SendEmailJob`  
✅ **Deadline support** — Job expires if not executed within 10 seconds  
✅ **Dead-letter handler** — Failed jobs trigger automatic fallback handler  
✅ **Zero config** — In-memory storage, minimal setup, runs immediately  

---

## Run

```bash
cd samples/MinimalApiSample
dotnet run
```

The app starts at `http://localhost:5000`.

---

## API Endpoints

### Enqueue a job

```bash
curl -X POST http://localhost:5000/send?email=user@example.com

# Response
{
  "jobId": "550e8400-e29b-41d4-a716-446655440000"
}
```

**To trigger failure**, use `email=fail@example.com`:

```bash
curl -X POST http://localhost:5000/send?email=fail@example.com
```

### Check job status

```bash
curl http://localhost:5000/job/550e8400-e29b-41d4-a716-446655440000

# Response
{
  "id": "550e8400-e29b-41d4-a716-446655440000",
  "status": "Succeeded",
  "attempts": 1,
  "deadline": "2026-03-31T12:00:15Z"
}
```

---

## What Happens (Flow)

### Success path:
1. **Enqueue** → Job added to queue with 10-second deadline
2. **Dispatch** → Worker picks up job immediately (wake-up signal)
3. **Execute** → SendEmailJob logs "Sending email..."
4. **Complete** → Job transitions to `Succeeded`

### Failure path:
1. **Enqueue** → Job added with `email=fail@example.com`
2. **Dispatch** → Worker picks up immediately
3. **Execute** → SendEmailJob throws exception (simulated failure)
4. **Retry** → Job scheduled for retry (attempt 2)
5. **Dispatch** → Worker retries after 1 second
6. **Execute** → Fails again
7. **Dead-letter** → Job exhausted retries, status = `Failed`
8. **Handler** → `SendEmailDeadLetterHandler.HandleAsync()` invoked
9. **Log** → Dead-letter handler logs "📧 Email job failed permanently..."

### Expired path:
1. **Enqueue** → Job with 10-second deadline
2. **Delay** → Job sits for >10 seconds without being picked up
3. **Fetch** → Worker picks it up after deadline has passed
4. **Expire** → Deadline check fails, job transitions to `Expired`, skipped
5. **No handler** → Dead-letter handler NOT invoked (expired, not failed)

---

## Code Highlights

### Job Definition (IJob<T>)
```csharp
public class SendEmailJob : IJob<SendEmailInput>
{
    public async Task ExecuteAsync(SendEmailInput input, CancellationToken ct)
        => await _email.SendAsync(input.Email, ct);
}
```

### Dead-Letter Handler
```csharp
public class SendEmailDeadLetterHandler : IDeadLetterHandler<SendEmailJob>
{
    public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken ct)
    {
        _logger.LogError("Email job failed after {Attempts} attempts", failedJob.Attempts);
        return Task.CompletedTask;
    }
}
```

### Enqueue with Deadline
```csharp
await scheduler.EnqueueAsync<SendEmailJob, SendEmailInput>(
    new(email),
    deadlineAfter: TimeSpan.FromSeconds(10));
```

---

## Key NexJob Features Demonstrated

| Feature | Location |
|---------|----------|
| **Async/await native** | `ExecuteAsync` is fully async |
| **DI integration** | Constructor injection of `ILogger` |
| **Type-safe jobs** | `IJob<SendEmailInput>` — no magic DTO |
| **Enqueue** | `scheduler.EnqueueAsync<SendEmailJob, SendEmailInput>()` |
| **Deadline** | `deadlineAfter: TimeSpan.FromSeconds(10)` |
| **Dead-letter handler** | `IDeadLetterHandler<SendEmailJob>` invoked on failure |
| **Job status** | `job.Status` shows Succeeded/Failed/Expired |
| **Wake-up dispatch** | Job executes immediately, not after polling delay |
| **Minimal config** | In-memory storage, 2 lines of setup |

---

## Explore Further

* Read [README.md](../../README.md) for full feature list
* Check [ARCHITECTURE.md](../../ARCHITECTURE.md) for design details
* See other samples in [samples/](../) directory
