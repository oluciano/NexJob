# Writing Tests

NexJob follows a multi-layered testing strategy to ensure both speed and reliability.

---

## Testing Pyramid & Strategy

| Layer | Project | Focus | 3N Applied | Environment |
| :--- | :--- | :--- | :--- | :--- |
| **Unit** | `*.Tests` | Isolated logic, branch coverage | Full (N1, N2, N3) | InMemory / Mocks |
| **Integration** | `*.IntegrationTests` | Happy path, infra contracts | N1 | Real Storage (Docker) |
| **Reliability** | `*.ReliabilityTests` | Chaos, concurrency, crash recovery | N2 (Complex failures) | Real Storage (Stress) |
| **Distributed** | `*.ReliabilityTests.Distributed` | Cluster coordination, failover | N2 (Network/Cluster) | Multi-node |

> **Case Study: Recurring Job Distributed Lock (N2 Distributed)**
> A unit test (N1/N2) can verify that the code *calls* `TryAcquireRecurringJobLockAsync`. However, only a **Distributed Reliability Test** can verify that when 5 instances of NexJob start at the exact same millisecond, exactly one instance enqueues the recurring job while the other 4 log a "lock not acquired" message. This prevents double-firing in production clusters.

### 1. Unit Tests (The Foundation)
Target 100% logic coverage per class. Use mocks (`Moq`) for external dependencies.
**Mandate:** 80% global line coverage floor for PR approval.

### 2. Integration Tests (Contract Validation)
Ensure implementations (Postgres, SQL Server, etc.) correctly fulfill `IJobStorage` and `IRecurringStorage` contracts. Focus on N1 (Happy Path) across all supported providers.

### 3. Reliability Tests (Hardening)
Test complex failure scenarios that cannot be easily mocked:
- **Crash Recovery:** Process death during execution.
- **High Concurrency:** Race conditions in `FetchNextAsync`.
- **Latency:** Signal-to-execution delay under load.

### 4. Distributed Reliability (Cluster)
Test multi-node invariants:
- **Distributed Locks:** Ensuring a recurring job only fires once in a cluster.
- **Orphaned Jobs:** Recovering jobs from a crashed node.

---

## Unit Testing Jobs

Test job logic directly by instantiating the job class and calling `ExecuteAsync`.

```csharp
public sealed class SendWelcomeEmailJobTests
{
    [Fact]
    public async Task ExecuteAsync_SendsEmail_WithCorrectAddress()
    {
        // Arrange
        var emailService = new FakeEmailService();
        var job = new SendWelcomeEmailJob(emailService);

        // Act
        await job.ExecuteAsync(CancellationToken.None);

        // Assert
        Assert.Single(emailService.SentEmails);
        Assert.Equal("user@example.com", emailService.SentEmails[0].To);
    }
}
```

---

## Integration Testing with InMemory Storage

The dispatcher, the recurring scheduler and the registration of configured recurring jobs are **hosted services**, so they only run inside a started host. `new ServiceCollection().BuildServiceProvider()` does not start them and no job would ever execute. Build a real host, start it, and wait on a signal from the job instead of sleeping.

```csharp
public sealed class JobIntegrationTests
{
    [Fact]
    public async Task EnqueueAndExecute_CompletesSuccessfully()
    {
        // Arrange
        var executed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(options => options.PollingInterval = TimeSpan.FromMilliseconds(50)); // InMemory by default
                services.AddSingleton(executed);
                services.AddTransient<TestJob>();
            })
            .Build();
        await host.StartAsync();

        // Act
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<TestJob>();

        // Assert
        (await executed.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();

        await host.StopAsync();
    }
}

public sealed class TestJob(TaskCompletionSource<bool> executed) : IJob
{
    public Task ExecuteAsync(CancellationToken ct)
    {
        executed.TrySetResult(true);
        return Task.CompletedTask;
    }
}
```

`AddNexJob()` returns a `NexJobBuilder`, not an `IServiceCollection`: register your own services on `services` (as above) instead of chaining them after `AddNexJob()`. You can register jobs one by one with `AddTransient<TJob>()` or scan an assembly with `AddNexJobJobs(assembly)`.

---

## Testing with Input

```csharp
[Fact]
public async Task EnqueueWithInput_PassesInputToJob()
{
    var received = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var host = Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddNexJob(options => options.PollingInterval = TimeSpan.FromMilliseconds(50));
            services.AddSingleton(received);
            services.AddTransient<ProcessorJob>();
        })
        .Build();
    await host.StartAsync();

    var scheduler = host.Services.GetRequiredService<IScheduler>();
    await scheduler.EnqueueAsync<ProcessorJob, ProcessInput>(new ProcessInput(42));

    Assert.Equal(42, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));

    await host.StopAsync();
}

public sealed record ProcessInput(int Value);

public sealed class ProcessorJob(TaskCompletionSource<int> received) : IJob<ProcessInput>
{
    public Task ExecuteAsync(ProcessInput input, CancellationToken ct)
    {
        received.TrySetResult(input.Value);
        return Task.CompletedTask;
    }
}
```

---

## Testing Retries

The default delay between attempts is 16 seconds or more, which would make a test crawl. Override `RetryDelayFactory` so retries are immediate:

```csharp
[Fact]
public async Task JobFailsThenRetries_SucceedsOnSecondAttempt()
{
    var succeeded = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var host = Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddNexJob(options =>
            {
                options.MaxAttempts = 3;
                options.PollingInterval = TimeSpan.FromMilliseconds(50);
                options.RetryDelayFactory = _ => TimeSpan.FromMilliseconds(50);
            });
            services.AddSingleton(succeeded);
            services.AddTransient<FlakyJob>();
        })
        .Build();
    await host.StartAsync();

    await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<FlakyJob>();

    // Succeeds on attempt 2: the first attempt failed and was retried
    Assert.Equal(2, await succeeded.Task.WaitAsync(TimeSpan.FromSeconds(5)));

    await host.StopAsync();
}

public sealed class FlakyJob(IJobContext context, TaskCompletionSource<int> succeeded) : IJob
{
    public Task ExecuteAsync(CancellationToken ct)
    {
        if (context.Attempt == 1)
        {
            throw new InvalidOperationException("first attempt fails");
        }

        succeeded.TrySetResult(context.Attempt);
        return Task.CompletedTask;
    }
}
```

Count attempts with `IJobContext.Attempt`, not with a field on the job: a job is a transient service, so every attempt gets a new instance.

---

## Testing Dead-Letter Handlers

```csharp
[Fact]
public async Task JobExhaustsRetries_InvokesDeadLetterHandler()
{
    var handler = new TestDeadLetterHandler();
    using var host = Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddNexJob(options =>
            {
                options.MaxAttempts = 2;
                options.PollingInterval = TimeSpan.FromMilliseconds(50);
                options.RetryDelayFactory = _ => TimeSpan.FromMilliseconds(50);
            });
            services.AddTransient<FailingJob>();
            services.AddTransient<IDeadLetterHandler<FailingJob>>(_ => handler);
        })
        .Build();
    await host.StartAsync();

    await host.Services.GetRequiredService<IScheduler>().EnqueueAsync<FailingJob>();

    await handler.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert.NotNull(handler.FailedJob);
    Assert.NotNull(handler.LastException);

    await host.StopAsync();
}

public sealed class FailingJob : IJob
{
    public Task ExecuteAsync(CancellationToken ct) => throw new InvalidOperationException("always fails");
}

public sealed class TestDeadLetterHandler : IDeadLetterHandler<FailingJob>
{
    public TaskCompletionSource Invoked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public JobRecord? FailedJob { get; private set; }

    public Exception? LastException { get; private set; }

    public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken ct)
    {
        FailedJob = failedJob;
        LastException = lastException;
        Invoked.TrySetResult();
        return Task.CompletedTask;
    }
}
```

---

## Testing Recurring Jobs

Recurring jobs declared with `options.AddRecurringJob` are registered by a hosted service when the host starts, so start the host before looking at storage:

```csharp
[Fact]
public async Task RecurringJob_IsRegisteredOnStartup()
{
    using var host = Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddNexJob(options => options.AddRecurringJob<TestJob>("test-recurring", "0 0 * * *"));
            services.AddSingleton(new TaskCompletionSource<bool>());
            services.AddTransient<TestJob>();
        })
        .Build();
    await host.StartAsync();
    await Task.Delay(500); // let the registration service run

    var storage = host.Services.GetRequiredService<IStorageProvider>();
    var recurring = await storage.GetRecurringJobsAsync();

    Assert.Contains(recurring, r => r.RecurringJobId == "test-recurring");

    await host.StopAsync();
}
```

---

## Testing Continuations

```csharp
[Fact]
public async Task ContinueWith_ChildExecutesAfterParentSucceeds()
{
    var childRan = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    using var host = Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddNexJob(options => options.PollingInterval = TimeSpan.FromMilliseconds(50));
            services.AddSingleton(childRan);
            services.AddTransient<ParentJob>();
            services.AddTransient<ChildJob>();
        })
        .Build();
    await host.StartAsync();

    var scheduler = host.Services.GetRequiredService<IScheduler>();
    var parentId = await scheduler.EnqueueAsync<ParentJob>();
    await scheduler.ContinueWithAsync<ChildJob>(parentId);

    Assert.True(await childRan.Task.WaitAsync(TimeSpan.FromSeconds(5)));

    await host.StopAsync();
}

public sealed class ParentJob : IJob
{
    public Task ExecuteAsync(CancellationToken ct) => Task.CompletedTask;
}

public sealed class ChildJob(TaskCompletionSource<bool> childRan) : IJob
{
    public Task ExecuteAsync(CancellationToken ct)
    {
        childRan.TrySetResult(true);
        return Task.CompletedTask;
    }
}
```

---

## Tips

- Wait on a `TaskCompletionSource` that the job completes (with `WaitAsync(timeout)`) instead of sleeping for a fixed time
- Lower `PollingInterval` and `RetryDelayFactory` in tests so nothing waits for the production defaults
- InMemory storage is fast and sufficient for unit tests
- Use Testcontainers for integration tests against real databases
- Keep tests deterministic — avoid real time delays where possible

---

## Next Steps

- [Common Scenarios](15-Common-Scenarios.md) — Real-world use cases
- [Troubleshooting](16-Troubleshooting.md) — Debug failing tests
- [Best Practices](13-Best-Practices.md) — Production guidelines
