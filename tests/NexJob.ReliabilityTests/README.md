# NexJob Reliability Tests

Production validation suite for NexJob background job processing framework.

## Purpose

This test project validates **real-world failure scenarios, recovery mechanisms, and system behavior under stress**. Unlike unit tests, these tests use:

- Real storage providers (InMemory)
- Real job dispatcher
- Real scheduler and execution pipeline
- No mocks or stubs

Tests are **marked with `[Trait("Category", "Reliability")]`** and are **NOT run in CI** by default.

---

## Test Categories

### 1. Retry + Dead-Letter Behavior ([RetryAndDeadLetterTests.cs](RetryAndDeadLetterTests.cs))

Validates retry execution and permanent failure handling:

- `RetryExecutesCorrectlyAfterFailure` — Job fails once, succeeds on retry with correct attempt count
- `DeadLetterHandlerInvokedAfterMaxAttemptsExhausted` — Handler called exactly once after all retries exhausted
- `DeadLetterHandlerReceivesCorrectJobContext` — Handler receives correct job record and exception
- `DeadLetterHandlerExceptionDoesNotCrashDispatcher` — Handler exceptions don't crash dispatcher
- `MultipleFailuresProgressThroughRetries` — Multiple failed attempts tracked correctly

**Key Insight**: Dead-letter handlers are critical for failure observability in production. These tests verify the complete failure chain.

### 2. Concurrency + Duplicate Prevention ([ConcurrencyTests.cs](ConcurrencyTests.cs))

Validates that jobs execute exactly once despite concurrent workers:

- `SingleJobNeverExecutesTwiceWithMultipleWorkers` — Single job with 3 workers executes once
- `ConcurrentEnqueueOfMultipleJobsExecutesAll` — 10 concurrent enqueues all execute
- `StressTest_LargeNumberOfJobsProcessedCorrectly` — 50 jobs processed under load
- `NoDeadlocksUnderConcurrentWork` — Continuous enqueue/execute cycle without deadlock
- `MultipleWorkersHandleMixedSuccessAndFailure` — Mixed success/fail jobs handled correctly

**Key Insight**: Concurrency is coordinated by storage, not dispatcher. Tests verify storage locking works correctly.

### 3. Crash & Recovery ([RecoveryTests.cs](RecoveryTests.cs))

Simulates dispatcher crashes and validates recovery:

- `JobNotLostOnDispatcherCrash` — Job persists in storage after abrupt stop
- `JobResumesAfterDispatcherRestart` — Job completes after dispatcher restarts
- `JobNotDuplicatedOnProcessingStateRecovery` — Processing jobs don't re-execute on restart
- `FailedJobWithPendingRetryResumesProperly` — Failed jobs with pending retries recover
- `StorageStateRemainsConsistentAcrossRestarts` — Complete state consistency validated

**Key Insight**: Storage is the source of truth. Dispatcher restart should never lose or duplicate jobs.

### 4. Deadline Expiration ([DeadlineTests.cs](DeadlineTests.cs))

Validates job expiration when deadline passes:

- `JobNotExecutedAfterDeadline` — Job marked Expired if deadline passes before execution
- `JobWithLongDeadlineExecutesNormally` — Normal jobs with long deadlines execute
- `DeadlineCheckOccursBeforeExecution` — Deadline checked before execution starts
- `ExpiredJobMetricIsRecorded` — Expiration metrics tracked correctly
- `MultipleExpiredJobsHandledCorrectly` — Batch expiration works correctly
- `DeadlineNotAppliedToScheduledJobs` — API constraint validation

**Key Insight**: Deadlines enable time-sensitive workflows. Tests verify enforcement before execution.

### 5. Wake-Up Latency ([WakeUpLatencyTests.cs](WakeUpLatencyTests.cs))

Validates near-zero latency for local enqueue:

- `LocalEnqueueWakesDispatcherImmediatelyWithHighPollingInterval` — <500ms execution with 30s polling
- `MultipleLocalEnqueuesResponsiveWithHighPolling` — Multiple jobs execute quickly despite high polling
- `WakeUpChannelNonBlocking` — Enqueue returns immediately without blocking
- `NoWakeUpNeededForDistributedScenario` — Documents polling fallback for distributed scenarios

**Key Insight**: Wake-up channel eliminates polling latency for local enqueue. Critical for interactive use cases.

### 6. Failure Diagnostics ([DiagnosticsTests.cs](DiagnosticsTests.cs))

Validates logging and diagnostic output:

- `JobExecutionLogsContainIdentifiers` — Logs include job type and input
- `FailedJobLogsIncludeExceptionDetails` — Exception messages appear in logs
- `DeadLetterHandlerExecutionIsLogged` — Handler invocation logged
- `SystemLogsCaptureDispatcherState` — Dispatcher state visible in logs

**Key Insight**: Actionable logs are essential for production troubleshooting.

### 7. Concurrent Enqueue Stress ([ConcurrentStressTests.cs](ConcurrentStressTests.cs))

High-concurrency load testing:

- `ConcurrentEnqueueOf100JobsAllExecute` — 100 concurrent jobs all execute
- `RapidEnqueueAndExecuteCycleWithoutDeadlock` — Sustained throughput without deadlock
- `ConcurrentEnqueueWithMixedJobTypes` — Multiple job types under concurrent load
- `ParallelEnqueueWithStructuredInputs` — 50 jobs with complex input types
- `HighConcurrencyWithFailureRecovery` — 40 jobs with failure+retry under concurrent load

**Key Insight**: System should scale to 100+ concurrent jobs without deadlock or data loss.

---

## Test Infrastructure

### Base Class: [ReliabilityTestBase.cs](ReliabilityTestBase.cs)

Provides common utilities:

- `BuildHost()` — Creates host with standard reliability test configuration (2 workers, fast retries)
- `WaitForJobStatus()` — Polls storage until job reaches expected status
- `GetJobCountByStatus()` — Counts jobs by status
- `ResetTestState()` — Clears test job static state

### Test Jobs: [SuccessJob.cs](SuccessJob.cs)

- `SuccessJob` — Succeeds immediately
- `AlwaysFailJob` — Always throws exception
- `TrackingJob` — Tracks execution count
- `DelayJob<TInput>` — Accepts DelayJobInput, delays execution
- `FailOnceThenSucceedJob` — Fails on first attempt, succeeds on second
- `CancellableJob` — Respects cancellation gracefully
- `DiagnosticJob<TInput>` — Logs diagnostic messages

### Test Handlers: [RecordingDeadLetterHandler.cs](RecordingDeadLetterHandler.cs)

- `RecordingDeadLetterHandler<T>` — Records invocation context
- `ThrowingDeadLetterHandler<T>` — Throws to verify exception handling
- `AsyncDeadLetterHandler<T>` — Tracks async operations

---

## Running the Tests

### All reliability tests:

```bash
dotnet test tests/NexJob.ReliabilityTests -c Release
```

### Specific test class:

```bash
dotnet test tests/NexJob.ReliabilityTests -c Release --filter "ClassName=NexJob.ReliabilityTests.RetryAndDeadLetterTests"
```

### Specific test:

```bash
dotnet test tests/NexJob.ReliabilityTests -c Release --filter "Name=RetryExecutesCorrectlyAfterFailure"
```

### With verbose output:

```bash
dotnet test tests/NexJob.ReliabilityTests -c Release --verbosity detailed
```

---

## Test Configuration

Tests use a standard host builder with:

| Setting | Value | Reason |
|---------|-------|--------|
| **Workers** | 2-5 | Concurrency testing without overwhelming machine |
| **MaxAttempts** | 3 | Fast failure→recovery cycles |
| **PollingInterval** | 50-100ms | Quick execution for testing |
| **RetryDelayFactory** | 100-200ms | Instant retries for fast test execution |

Each test can override these with custom builders.

---

## Limitations & Assumptions

1. **Storage**: Tests use InMemory storage. Distributed storage providers (Postgres, MongoDB) not tested here.
2. **Timing**: Flakiness can occur on slow machines. Increase timeouts if tests fail locally.
3. **Network**: No network-based failure scenarios (storage unavailable, etc.).
4. **Cancellation**: Tests simulate graceful cancellation via CancellationToken, not abrupt process kill.
5. **Scale**: Tests validate up to 100 concurrent jobs. Enterprise scale (10k+) requires load test infrastructure.

---

## When to Run These Tests

✅ **Before major releases** — Validate production readiness  
✅ **After architectural changes** — Verify system behavior unchanged  
✅ **When adding new storage providers** — Validate integration  
✅ **When investigating reported issues** — Add regression test  

❌ **In CI pipeline** — Tests are too slow and timing-dependent  
❌ **On every commit** — Run unit tests instead  

---

## Interpreting Failures

### Timeout Failures

If a test times out:

1. Check if your machine is under load (`top`, `Task Manager`)
2. Increase timeouts in the test (e.g., `TimeSpan.FromSeconds(15)`)
3. Reduce concurrency (lower worker count or job count)

Example:
```csharp
var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, 
    TimeSpan.FromSeconds(15)); // Increased from 10
```

### State Inconsistency Failures

If a test reports unexpected job status:

1. Verify storage implementation is thread-safe
2. Check that all state transitions are persisted
3. Review dispatcher logs for errors

### Deadlock Failures

If a concurrent test hangs:

1. Add logging to identify which workers are stuck
2. Reduce concurrent load to isolate issue
3. Review storage locking strategy

---

## Example: Adding a New Reliability Test

```csharp
[Trait("Category", "Reliability")]
public sealed class YourNewTests : ReliabilityTestBase
{
    [Fact]
    public async Task YourScenario()
    {
        ResetTestState();
        
        using var host = BuildHost(s =>
        {
            s.AddTransient<YourJob>();
        }, workers: 2);
        
        await host.StartAsync();
        
        var scheduler = host.Services.GetRequiredService<IScheduler>();
        var jobId = await scheduler.EnqueueAsync<YourJob>();
        
        // Wait for job to complete
        var job = await WaitForJobStatus(host, jobId, JobStatus.Succeeded, 
            TimeSpan.FromSeconds(5));
        
        job.Should().NotBeNull();
        
        await host.StopAsync();
    }
}
```

---

## CLAUDE.md Compliance

All tests follow NexJob architectural constraints:

✅ Real storage (no mocks)  
✅ Real dispatcher (no stubs)  
✅ Stateless execution (restart safe)  
✅ Storage as source of truth  
✅ No static/global state (except test tracking)  
✅ Complete async/await (no `.Result` or `.Wait()`)  
✅ CancellationToken propagation  
✅ Zero warnings in Release builds  

---

## Metrics

**Coverage**: 34 tests across 7 categories  
**Duration**: ~25 seconds (full run)  
**Language**: C# 12, .NET 8.0  
**Dependencies**: xUnit, FluentAssertions, Microsoft.Extensions.Hosting  

---

## Related Documentation

- [ARCHITECTURE.md](../../ARCHITECTURE.md) — System design and guarantees
- [CLAUDE.md](../../CLAUDE.md) — Runtime constraints and rules
- [CONTRIBUTING.md](../../CONTRIBUTING.md) — Engineering standards
