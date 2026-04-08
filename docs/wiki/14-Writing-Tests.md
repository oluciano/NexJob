# Writing Tests

Test jobs reliably without mocks where possible. Use InMemory storage for fast, deterministic tests.

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

Test the full pipeline: enqueue → dispatch → execute → complete.

```csharp
public sealed class JobIntegrationTests
{
    [Fact]
    public async Task EnqueueAndExecute_CompletesSuccessfully()
    {
        // Arrange
        var services = new ServiceCollection()
            .AddNexJob() // InMemory by default
            .AddNexJobJobs(typeof(TestJob).Assembly)
            .AddSingleton<ITestService, FakeTestService>()
            .BuildServiceProvider();

        var scheduler = services.GetRequiredService<IScheduler>();

        // Act
        await scheduler.EnqueueAsync<TestJob>(cancellationToken: CancellationToken.None);

        // Wait for dispatcher to process
        await Task.Delay(500);

        // Assert
        var testService = (FakeTestService)services.GetRequiredService<ITestService>();
        Assert.True(testService.WasExecuted);
    }
}

public sealed class TestJob : IJob
{
    private readonly ITestService _test;
    public TestJob(ITestService test) => _test = test;
    public async Task ExecuteAsync(CancellationToken ct) => await _test.ExecuteAsync(ct);
}
```

---

## Testing with Input

```csharp
[Fact]
public async Task EnqueueWithInput_PassesInputToJob()
{
    var services = new ServiceCollection()
        .AddNexJob()
        .AddNexJobJobs(typeof(ProcessorJob).Assembly)
        .AddSingleton<IProcessor, FakeProcessor>()
        .BuildServiceProvider();

    var scheduler = services.GetRequiredService<IScheduler>();

    var input = new ProcessInput(42);
    await scheduler.EnqueueAsync<ProcessorJob, ProcessInput>(input, cancellationToken: CancellationToken.None);

    await Task.Delay(500);

    var processor = (FakeProcessor)services.GetRequiredService<IProcessor>();
    Assert.Equal(42, processor.ProcessedValue);
}
```

---

## Testing Retries

```csharp
[Fact]
public async Task JobFailsThenRetries_SucceedsOnSecondAttempt()
{
    var fakeService = new FakeFlakyService { FailCount = 1 }; // Fails once

    var services = new ServiceCollection()
        .AddNexJob(options => options.MaxAttempts = 3)
        .AddNexJobJobs(typeof(FlakyJob).Assembly)
        .AddSingleton<IFlakyService>(fakeService)
        .BuildServiceProvider();

    var scheduler = services.GetRequiredService<IScheduler>();
    await scheduler.EnqueueAsync<FlakyJob>(cancellationToken: CancellationToken.None);

    // Wait for retries to complete
    await Task.Delay(2000);

    Assert.Equal(2, fakeService.CallCount); // Called twice: fail + success
}
```

---

## Testing Dead-Letter Handlers

```csharp
[Fact]
public async Task JobExhaustsRetries_InvokesDeadLetterHandler()
{
    var handler = new TestDeadLetterHandler();

    var services = new ServiceCollection()
        .AddNexJob(options => options.MaxAttempts = 2)
        .AddNexJobJobs(typeof(FailingJob).Assembly)
        .AddTransient<IDeadLetterHandler<FailingJob>, TestDeadLetterHandler>(_ => handler)
        .BuildServiceProvider();

    var scheduler = services.GetRequiredService<IScheduler>();
    await scheduler.EnqueueAsync<FailingJob>(cancellationToken: CancellationToken.None);

    await Task.Delay(1000);

    Assert.NotNull(handler.FailedJob);
    Assert.NotNull(handler.LastException);
}

private sealed class TestDeadLetterHandler : IDeadLetterHandler<FailingJob>
{
    public JobRecord? FailedJob { get; private set; }
    public Exception? LastException { get; private set; }

    public Task HandleAsync(JobRecord job, Exception ex, CancellationToken ct)
    {
        FailedJob = job;
        LastException = ex;
        return Task.CompletedTask;
    }
}
```

---

## Testing Recurring Jobs

```csharp
[Fact]
public async Task RecurringJob_CreatesJobOnSchedule()
{
    var services = new ServiceCollection()
        .AddNexJob(options =>
        {
            options.AddRecurringJob<TestJob>("test-recurring", "0 0 * * *");
        })
        .AddNexJobJobs(typeof(TestJob).Assembly)
        .BuildServiceProvider();

    await Task.Delay(1000); // Let recurring scheduler register

    // Verify recurring job was registered
    var storage = services.GetRequiredService<IStorageProvider>();
    var recurring = await storage.GetAllRecurringJobsAsync(CancellationToken.None);

    Assert.Contains(recurring, r => r.Id == "test-recurring");
}
```

---

## Testing Continuations

```csharp
[Fact]
public async Task ContinueWith_ChildExecutesAfterParentSucceeds()
{
    var services = new ServiceCollection()
        .AddNexJob()
        .AddNexJobJobs(typeof(ParentJob).Assembly, typeof(ChildJob).Assembly)
        .AddSingleton<ITracker, Tracker>()
        .BuildServiceProvider();

    var scheduler = services.GetRequiredService<IScheduler>();

    var parentId = await scheduler.EnqueueAsync<ParentJob>(cancellationToken: CancellationToken.None);
    await scheduler.ContinueWithAsync<ChildJob>(parentId, cancellationToken: CancellationToken.None);

    await Task.Delay(1000);

    var tracker = (Tracker)services.GetRequiredService<ITracker>();
    Assert.True(tracker.ParentExecuted);
    Assert.True(tracker.ChildExecuted);
}
```

---

## Tips

- Use `Task.Delay()` to wait for dispatcher — for production tests, consider a polling helper
- InMemory storage is fast and sufficient for unit tests
- Use Testcontainers for integration tests against real databases
- Keep tests deterministic — avoid real time delays where possible

---

## Next Steps

- [Common Scenarios](15-Common-Scenarios.md) — Real-world use cases
- [Troubleshooting](16-Troubleshooting.md) — Debug failing tests
- [Best Practices](13-Best-Practices.md) — Production guidelines
