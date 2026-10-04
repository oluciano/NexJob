---
title: "Write Unit and Integration Tests for NexJob Background Jobs"
sidebarTitle: "Writing Tests"
description: "Test NexJob background jobs with unit tests using mock schedulers and integration tests using InMemory storage for end-to-end verification."
---

NexJob jobs are plain .NET classes, which makes them straightforward to test at multiple layers. Unit tests call `ExecuteAsync` directly with fakes for dependencies. Integration tests spin up a real host with InMemory storage and verify that the full enqueue-dispatch-execute path works correctly. Use both layers together to get fast feedback on business logic and confidence in end-to-end behaviour.

## Testing Layers

| Layer | What it tests | Storage | Speed |
|---|---|---|---|
| **Unit** | Isolated job logic, branch coverage | Mocks / fakes | Very fast |
| **Integration** | Enqueue, dispatch, execution flow | InMemory | Fast |
| **Integration (real DB)** | Storage provider contracts | Docker / Testcontainers | Slower |
| **Reliability** | Crash recovery, race conditions | Real storage under stress | Slow |

## Unit Testing a Job

Test job logic directly by instantiating the class and calling `ExecuteAsync`. Inject fakes or mocks for every dependency.

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

Unit tests run without any NexJob infrastructure — no host, no scheduler, no storage. They are the fastest way to verify business logic and cover edge cases.

## Integration Testing with InMemory Storage

The dispatcher, recurring scheduler, and registered recurring jobs are **hosted services** — they only run inside a started host. Build a real host, start it, then wait on a `TaskCompletionSource` that the job signals when it finishes. Never sleep for a fixed duration; always use `WaitAsync(timeout)` so tests fail fast if something goes wrong.

```csharp
public sealed class JobIntegrationTests
{
    [Fact]
    public async Task EnqueueAndExecute_CompletesSuccessfully()
    {
        var executed = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(options =>
                    options.PollingInterval = TimeSpan.FromMilliseconds(50)); // InMemory by default
                services.AddSingleton(executed);
                services.AddTransient<TestJob>();
            })
            .Build();

        await host.StartAsync();

        var scheduler = host.Services.GetRequiredService<IScheduler>();
        await scheduler.EnqueueAsync<TestJob>();

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

!!! note
    `AddNexJob()` returns a `NexJobBuilder`, not an `IServiceCollection`. Register your own services on `services` directly, not by chaining onto `AddNexJob()`. Register jobs one by one with `AddTransient<TJob>()` or scan an assembly with `AddNexJobJobs(assembly)`.


## Testing Jobs with Input

```csharp
[Fact]
public async Task EnqueueWithInput_PassesInputToJob()
{
    var received = new TaskCompletionSource<int>(
        TaskCreationOptions.RunContinuationsAsynchronously);

    using var host = Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddNexJob(options =>
                options.PollingInterval = TimeSpan.FromMilliseconds(50));
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

## Testing Retries

The default retry delay is 16 seconds or more — far too long for a test. Override `RetryDelayFactory` to make retries near-instant.

```csharp
[Fact]
public async Task JobFailsThenRetries_SucceedsOnSecondAttempt()
{
    var succeeded = new TaskCompletionSource<int>(
        TaskCreationOptions.RunContinuationsAsynchronously);

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

    // Succeeds on attempt 2
    Assert.Equal(2, await succeeded.Task.WaitAsync(TimeSpan.FromSeconds(5)));

    await host.StopAsync();
}

public sealed class FlakyJob(IJobContext context, TaskCompletionSource<int> succeeded) : IJob
{
    public Task ExecuteAsync(CancellationToken ct)
    {
        if (context.Attempt == 1)
            throw new InvalidOperationException("first attempt fails");

        succeeded.TrySetResult(context.Attempt);
        return Task.CompletedTask;
    }
}
```

!!! warning
    Use `IJobContext.Attempt` to count attempts — never a field on the job class. Jobs are transient services, so every attempt creates a new instance and instance fields reset.


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
    public Task ExecuteAsync(CancellationToken ct) =>
        throw new InvalidOperationException("always fails");
}

public sealed class TestDeadLetterHandler : IDeadLetterHandler<FailingJob>
{
    public TaskCompletionSource Invoked { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

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

## Testing Recurring Jobs

Recurring jobs registered with `options.AddRecurringJob` are created by a hosted service on startup. Start the host before inspecting storage.

```csharp
[Fact]
public async Task RecurringJob_IsRegisteredOnStartup()
{
    using var host = Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddNexJob(options =>
                options.AddRecurringJob<TestJob>("test-recurring", "0 0 * * *"));
            services.AddSingleton(new TaskCompletionSource<bool>());
            services.AddTransient<TestJob>();
        })
        .Build();

    await host.StartAsync();
    await Task.Delay(500); // allow the registration hosted service to run

    var storage = host.Services.GetRequiredService<IStorageProvider>();
    var recurring = await storage.GetRecurringJobsAsync();

    Assert.Contains(recurring, r => r.RecurringJobId == "test-recurring");

    await host.StopAsync();
}
```

## Testing Continuations

```csharp
[Fact]
public async Task ContinueWith_ChildExecutesAfterParentSucceeds()
{
    var childRan = new TaskCompletionSource<bool>(
        TaskCreationOptions.RunContinuationsAsynchronously);

    using var host = Host.CreateDefaultBuilder()
        .ConfigureServices(services =>
        {
            services.AddNexJob(options =>
                options.PollingInterval = TimeSpan.FromMilliseconds(50));
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

## Tips

<div class="grid cards" markdown>
  -   **Signal, don't sleep**

    Always wait on a `TaskCompletionSource` that the job completes, then call `.WaitAsync(timeout)`. Fixed `Task.Delay` calls make tests slow and flaky.

  -   **Speed up test settings**

    Set `PollingInterval` to 50 ms and `RetryDelayFactory` to return near-zero values. Production defaults make tests crawl.

  -   **InMemory for unit tests**

    InMemory storage is fast, requires no infrastructure, and is the right choice for unit and most integration tests.

  -   **Testcontainers for DB tests**

    Use Testcontainers to spin up real Postgres or SQL Server instances for storage contract tests, keeping them isolated from your CI environment.

</div>
