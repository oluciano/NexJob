---
title: "NexJob Quick Start: Run Your First .NET Background Job"
sidebarTitle: "Quick Start"
description: "Install NexJob, define your first job, and enqueue it in a .NET 8 project. Covers ASP.NET Core and Worker Service setups with runnable samples."
---

NexJob needs three things to run: a package reference, a service registration, and a job class. In about five minutes you will have a job running in your own project.

## Before you start

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download) and a project to add NexJob to. For a new one, `dotnet new worker -n MyJobs` creates a Worker Service, and `dotnet new web -n MyJobs` creates an ASP.NET Core app.

## Your first job

### 1. Install the package

```bash
dotnet add package NexJob
```

The core package includes the dispatcher, the scheduler and an InMemory storage provider, ready to use with no further configuration.

### 2. Register the services

Call `AddNexJob()` once, then scan your assembly so NexJob can discover your job classes.

=== "Worker Service"

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

    // Step 4 goes here

    await host.WaitForShutdownAsync();
    ```

=== "ASP.NET Core"

    ```csharp
    // Program.cs
    using NexJob;

    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddNexJob()
                    .AddNexJobJobs(typeof(Program).Assembly);

    var app = builder.Build();

    // Step 4 goes here

    app.Run();
    ```

!!! tip
    Call `AddNexJob()` exactly once. Calling it multiple times registers duplicate background services and causes undefined behavior.

### 3. Define a job

Implement `IJob` for work that needs no input. NexJob resolves the job from the DI container, so constructor injection works.

```csharp
// HelloJob.cs
public sealed class HelloJob : IJob
{
    public Task ExecuteAsync(CancellationToken ct)
    {
        Console.WriteLine($"Hello at {DateTimeOffset.UtcNow:O}");
        return Task.CompletedTask;
    }
}
```

### 4. Enqueue the job

Resolve `IScheduler` from DI and call `EnqueueAsync`. The dispatcher picks the job up right away in the same process, through the wake-up channel.

```csharp
var scheduler = host.Services.GetRequiredService<IScheduler>();   // Worker Service
// var scheduler = app.Services.GetRequiredService<IScheduler>(); // ASP.NET Core

await scheduler.EnqueueAsync<HelloJob>();
```

### 5. Run it

```bash
dotnet run
```

You will see the job's output in the console:

```text
Hello at 2026-10-11T13:05:29.7182279+00:00
```

The dispatcher runs as a hosted `BackgroundService` in your own process. There is no separate worker process or sidecar.

## Going further

### A job with typed input

Implement `IJob<T>` when the job needs structured input, and use a `record` for the input: it serializes cleanly and is immutable. The job below sends an e-mail through a service you register in DI (here a stand-in that writes to the console).

```csharp
public interface IEmailService
{
    Task SendAsync(string to, string subject, CancellationToken ct);
}

public sealed class ConsoleEmailService : IEmailService
{
    public Task SendAsync(string to, string subject, CancellationToken ct)
    {
        Console.WriteLine($"To: {to} | {subject}");
        return Task.CompletedTask;
    }
}

public sealed class SendWelcomeEmailJob : IJob<SendWelcomeEmailInput>
{
    private readonly IEmailService _email;

    public SendWelcomeEmailJob(IEmailService email) => _email = email;

    public async Task ExecuteAsync(SendWelcomeEmailInput input, CancellationToken ct)
        => await _email.SendAsync(input.Email, $"Welcome, {input.UserName}!", ct);
}

public sealed record SendWelcomeEmailInput(string Email, string UserName);
```

Register the service next to `AddNexJob()` (`services.AddSingleton<IEmailService, ConsoleEmailService>();`) and enqueue with the input:

```csharp
await scheduler.EnqueueAsync<SendWelcomeEmailJob, SendWelcomeEmailInput>(
    new SendWelcomeEmailInput("user@example.com", "Jane"));
```

### Expire a job that starts too late

Set a deadline and the job expires on its own if the workers are too busy to start it in time:

```csharp
await scheduler.EnqueueAsync<SendWelcomeEmailJob, SendWelcomeEmailInput>(
    new SendWelcomeEmailInput("user@example.com", "Jane"),
    deadlineAfter: TimeSpan.FromMinutes(5));
```

!!! note
    The deadline is checked **before** execution begins, not during. A job enqueued with `deadlineAfter: TimeSpan.FromMinutes(5)` that has not started within 5 minutes is marked `Expired` and never executes.

### Keep jobs across restarts

InMemory storage is for development and tests: jobs are lost when the process stops. For production, add a persistent provider:

```bash
dotnet add package NexJob.Postgres    # or NexJob.SqlServer, NexJob.Redis, NexJob.MongoDB
```

Register the provider **before** `AddNexJob()` so it replaces the InMemory default:

```csharp
using NexJob;
using NexJob.Postgres;

builder.Services.AddNexJobPostgres(
    "Host=localhost;Database=nexjob;Username=postgres;Password=secret");

builder.Services.AddNexJob(options =>
{
    options.Workers = 20;
    options.MaxAttempts = 5;
})
.AddNexJobJobs(typeof(Program).Assembly);
```

See [Storage Providers](storage/overview.md) for the other databases.

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
