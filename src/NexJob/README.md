# NexJob

Background jobs for .NET 8+. **Predictable. Observable. No magic.**

NexJob gives you reliable execution, retries with backoff, deadline enforcement and dead-letter handling, with a built-in dashboard and OpenTelemetry. If a job must run, fail safely and leave a trace, NexJob treats that as a first-class concern.

---

## Installation

```bash
dotnet add package NexJob
```

`NexJob` ships with an in-memory storage, which is enough to try it out. For production, add a storage provider package (see below).

---

## Quick Start

```csharp
// 1. Register NexJob and scan for jobs
builder.Services.AddNexJob();
builder.Services.AddNexJobJobs(typeof(Program).Assembly);
```

```csharp
// 2. Define a job
public sealed class SendInvoiceJob : IJob<SendInvoiceInput>
{
    private readonly IEmailService _email;

    public SendInvoiceJob(IEmailService email) => _email = email;

    public async Task ExecuteAsync(SendInvoiceInput input, CancellationToken ct)
        => await _email.SendAsync(input.Email, "Your invoice", ct);
}

public sealed record SendInvoiceInput(string Email);
```

```csharp
// 3. Enqueue from anywhere
var scheduler = app.Services.GetRequiredService<IScheduler>();
await scheduler.EnqueueAsync<SendInvoiceJob, SendInvoiceInput>(
    new SendInvoiceInput("customer@example.com"),
    deadlineAfter: TimeSpan.FromMinutes(5));
```

The job expires if it does not start within 5 minutes: no silent failures, no zombie jobs.

---

## Add what you need

| Need | Package |
|---|---|
| Storage | `NexJob.Postgres`, `NexJob.SqlServer`, `NexJob.Redis`, `NexJob.MongoDB` |
| Dashboard | `NexJob.Dashboard` (ASP.NET Core), `NexJob.Dashboard.Standalone` (worker services) |
| Telemetry | `NexJob.OpenTelemetry` |
| Event triggers | `NexJob.Kafka`, `NexJob.RabbitMQ`, `NexJob.Trigger.AwsSqs`, `NexJob.Trigger.AzureServiceBus`, `NexJob.Trigger.GooglePubSub`, `NexJob.Trigger.Salesforce`, `NexJob.Trigger.SalesforceStreaming` |
| Roslyn Analyzers | Built directly into `NexJob` (real-time compile-time guardrails in IDE) |

---

## Learn more

- **Documentation:** https://oluciano.github.io/NexJob/
- **What happens when something fails:** https://oluciano.github.io/NexJob/concepts/delivery-guarantees/
- **Retries and dead-letter handling:** https://oluciano.github.io/NexJob/concepts/retries-and-dead-letter/
- **Protect a queue (circuit breaker, throttling):** https://oluciano.github.io/NexJob/guides/circuit-breaker/
- **Get alerts when a job fails for good:** https://oluciano.github.io/NexJob/guides/alerts/
- **Source, issues and full README:** https://github.com/oluciano/NexJob
- **Changelog:** https://github.com/oluciano/NexJob/blob/main/CHANGELOG.md

Released under the MIT license.
