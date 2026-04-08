# Getting Started

Get NexJob running in under 2 minutes.

---

## 1. Install

```bash
dotnet add package NexJob
```

Choose a storage provider (or use the default InMemory):

```bash
dotnet add package NexJob.Postgres
dotnet add package NexJob.SqlServer
dotnet add package NexJob.Redis
dotnet add package NexJob.MongoDB
```

---

## 2. Configure

```csharp
using NexJob;

var builder = WebApplication.CreateBuilder(args);

// Register NexJob with InMemory storage (default)
builder.Services.AddNexJob();

// Or with a specific storage provider
builder.Services.AddNexJob(options =>
{
    options.UsePostgres("Host=localhost;Database=nexjob;Username=postgres;Password=secret");
    options.Workers = 20;
    options.MaxAttempts = 5;
});

// Register all jobs from your assembly
builder.Services.AddNexJobJobs(typeof(Program).Assembly);

var app = builder.Build();
app.Run();
```

---

## 3. Define a Job

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

Or with input:

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

---

## 4. Enqueue and Run

```csharp
var scheduler = app.Services.GetRequiredService<IScheduler>();

// Simple job (no input)
await scheduler.EnqueueAsync<SendWelcomeEmailJob>(cancellationToken: ct);

// Job with input
await scheduler.EnqueueAsync<SendWelcomeEmailJob, SendWelcomeEmailInput>(
    new SendWelcomeEmailInput("user@example.com", "John"),
    cancellationToken: ct);
```

That's it. The dispatcher will pick up the job and execute it automatically.

---

## Full Minimal Example (Worker Service)

```csharp
// Program.cs
using NexJob;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddNexJob();
        services.AddNexJobJobs(typeof(Program).Assembly);
        services.AddSingleton<ILogger>(new ConsoleLogger());
    })
    .Build();

await host.RunAsync();
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

Run:

```bash
dotnet run
```

Output:
```
Hello at 2026-04-08T12:00:00Z
```

---

## Next Steps

- [Mental Model](00-Mental-Model.md) — Understand how NexJob works
- [Job Types](02-Job-Types.md) — `IJob` vs `IJob<T>` in detail
- [Scheduling](03-Scheduling.md) — Delay, schedule, priority, deadline
- [Storage Providers](09-Storage-Providers.md) — Configure PostgreSQL, Redis, etc.
