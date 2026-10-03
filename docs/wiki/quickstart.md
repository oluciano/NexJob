---
title: "NexJob Quick Start: Run Your First .NET Background Job"
sidebarTitle: "Quick Start"
description: "Install NexJob, define your first job, and enqueue it in a .NET 8 project. Covers ASP.NET Core and Worker Service setups with runnable samples."
---

NexJob needs three things to run: a package reference, a service registration, and a job class. This guide walks you through each step, shows you both ASP.NET Core and Worker Service setups, and links you to runnable reference samples in the repository so you can inspect real, working projects immediately.


  #### Install the package

Add the core NexJob package to your .NET 8 project:

    ```bash
    dotnet add package NexJob
    ```

    The core package includes the dispatcher, scheduler, and an InMemory storage provider that is ready to use with no further configuration. For production workloads, add one of the persistent storage providers:

    ```bash
    # PostgreSQL
    dotnet add package NexJob.Postgres

    # SQL Server
    dotnet add package NexJob.SqlServer

    # Redis
    dotnet add package NexJob.Redis

    # MongoDB
    dotnet add package NexJob.MongoDB
    ```

  #### Register services in Program.cs

Call `AddNexJob()` and scan your assembly so NexJob can discover your job classes via dependency injection.

    **InMemory (default — great for development and testing):**

    ```csharp
    using NexJob;

    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddNexJob()
                   .AddNexJobJobs(typeof(Program).Assembly);

    var app = builder.Build();
    app.Run();
    ```

    **PostgreSQL (persistent storage for production):**

    Register the storage provider **before** calling `AddNexJob()` so it replaces the InMemory default:

    ```csharp
    using NexJob;
    using NexJob.Postgres;

    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddNexJobPostgres(
        "Host=localhost;Database=nexjob;Username=postgres;Password=secret");

    builder.Services.AddNexJob(options =>
    {
        options.Workers = 20;
        options.MaxAttempts = 5;
    })
    .AddNexJobJobs(typeof(Program).Assembly);

    var app = builder.Build();
    app.Run();
    ```

    !!! tip
    Call `AddNexJob()` exactly once. Calling it multiple times registers duplicate background services and causes undefined behavior.

  #### Define a job

Implement `IJob` for parameterless work or `IJob<T>` when the job needs structured input. Both interfaces support constructor injection — NexJob resolves your dependencies from the DI container automatically.

    **Parameterless job (`IJob`):**

    ```csharp
    public sealed class SendWelcomeEmailJob : IJob
    {
        private readonly IEmailService _email;

        public SendWelcomeEmailJob(IEmailService email) => _email = email;

        public async Task ExecuteAsync(CancellationToken ct)
        {
            await _email.SendAsync("user@example.com", "Welcome!", ct);
        }
    }
    ```

    **Job with typed input (`IJob<T>`):**

    ```csharp
    public sealed class SendWelcomeEmailJob : IJob<SendWelcomeEmailInput>
    {
        private readonly IEmailService _email;

        public SendWelcomeEmailJob(IEmailService email) => _email = email;

        public async Task ExecuteAsync(SendWelcomeEmailInput input, CancellationToken ct)
        {
            await _email.SendAsync(input.Email, "Welcome!", ct);
        }
    }

    public sealed record SendWelcomeEmailInput(string Email, string UserName);
    ```

    Use a `record` for the input type — it serializes cleanly and is immutable by default.

  #### Enqueue the job

Resolve `IScheduler` from DI and call `EnqueueAsync`. The dispatcher picks up the job immediately on the same process via the wake-up channel.

    ```csharp
    var scheduler = app.Services.GetRequiredService<IScheduler>();

    // Parameterless job
    await scheduler.EnqueueAsync<SendWelcomeEmailJob>(cancellationToken: ct);

    // Job with input
    await scheduler.EnqueueAsync<SendWelcomeEmailJob, SendWelcomeEmailInput>(
        new SendWelcomeEmailInput("user@example.com", "Jane"),
        cancellationToken: ct);
    ```

    You can also set a deadline so the job expires automatically if the worker is too busy to start it in time:

    ```csharp
    await scheduler.EnqueueAsync<SendWelcomeEmailJob, SendWelcomeEmailInput>(
        new SendWelcomeEmailInput("user@example.com", "Jane"),
        deadlineAfter: TimeSpan.FromMinutes(5),
        cancellationToken: ct);
    ```

    !!! note
    The deadline is checked **before** execution begins, not during. A job enqueued with `deadlineAfter: TimeSpan.FromMinutes(5)` that has not started within 5 minutes is marked `Expired` and never executes.

  #### Run the application

Start your application as normal:

    ```bash
    dotnet run
    ```

    NexJob starts the dispatcher as a hosted `BackgroundService`. Once the job is enqueued, you will see output like:

    ```
    Hello at 2026-04-08T12:00:00Z
    ```

    The dispatcher runs on the same process — no separate worker process or sidecar required.



## Full minimal example (Worker Service)

The following is a complete, self-contained Worker Service that enqueues one job and waits for shutdown. No ASP.NET Core, no HTTP pipeline — just the job host.

```csharp
// Program.cs
using NexJob;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddNexJob();
        services.AddNexJobJobs(typeof(Program).Assembly);
    })
    .Build();

await host.StartAsync();

var scheduler = host.Services.GetRequiredService<IScheduler>();
await scheduler.EnqueueAsync<HelloJob>();

await host.WaitForShutdownAsync();
```

```csharp
// HelloJob.cs
public sealed class HelloJob : IJob
{
    public async Task ExecuteAsync(CancellationToken ct)
    {
        Console.WriteLine($"Hello at {DateTimeOffset.UtcNow}");
        await Task.CompletedTask;
    }
}
```

Run it:

```bash
dotnet run
```

Expected output:

```
Hello at 2026-04-08T12:00:00Z
```

## Runnable reference samples

The repository ships with ready-to-run reference architectures covering every NexJob capability. Clone the repo and `dotnet run` any sample directly.

| Sample | Focus | Port |
|---|---|---|
| [`NexJob.Sample.MinimalApi`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.MinimalApi) | Dashboard, dead-letter handler, deadline enforcement | 5001 |
| [`NexJob.Sample.WebApi`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.WebApi) | PostgreSQL storage, REST endpoints, `.http` test files | 5002 |
| [`NexJob.Sample.WorkerService`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.WorkerService) | Headless worker with standalone embedded dashboard | 5005 |
| [`NexJob.Sample.ConfiguredRecurring`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.ConfiguredRecurring) | Recurring jobs declared in `appsettings.json` | 5004 |
| [`NexJob.Sample.RabbitMQ`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.RabbitMQ) | Resilient outbox producer + trigger consumer | 5009 |
| [`NexJob.Sample.Kafka`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.Kafka) | Partitioned outbox publishing + consumer trigger | 5010 |
| [`NexJob.Sample.Reliability`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.Reliability) | Retry, checkpoint, deadline, dead-letter, circuit breaker, job control, health | 5011 |
| [`NexJob.Sample.Providers`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.Providers) | The same app on PostgreSQL, SQL Server, Redis, MongoDB or InMemory | 5012 |
| [`NexJob.Sample.Storage`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.Storage) | PostgreSQL read replica, distributed throttle, OTel | 5007 |
| [`NexJob.Sample.CloudTriggers`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.CloudTriggers) | AWS SQS, Azure Service Bus, GCP Pub/Sub, Salesforce | 5008 |

A full local test stack (PostgreSQL 16, Redis 7, RabbitMQ 3.13, Kafka KRaft, SQL Server 2022 and MongoDB 7) is provided in [`samples/docker-compose.yml`](https://github.com/oluciano/NexJob/tree/develop/samples/docker-compose.yml).

## Next steps

<div class="grid cards" markdown>
  -   [**Mental Model**](mental-model.md)

    Understand storage-first design, the job state machine, wake-up channel, and crash recovery.

  -   [**Storage Providers**](storage/overview.md)

    Configure PostgreSQL, SQL Server, Redis, or MongoDB for production workloads.

  -   [**Scheduling**](concepts/scheduling.md)

    Enqueue, schedule with delay, set deadlines, and chain job continuations.

  -   [**Retries & Dead Letter**](concepts/retries-and-dead-letter.md)

    Configure exponential backoff and handle exhausted retries gracefully.

</div>
