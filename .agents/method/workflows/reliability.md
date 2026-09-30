# Workflow: Reliability Testing

**When:** Validating critical scenarios under failure, concurrency, and distributed conditions

---

## Entry Criteria

- Scenario involves critical invariants (deadline, retry, crash recovery)
- Testing requires real storage and distributed conditions
- Deterministic testing in isolation insufficient

---

## Scope of Reliability Tests

### Covered Scenarios

- **Retry & Dead-Letter:** Retry execution, handler invocation, exception resilience
- **Concurrency:** Duplicate prevention, concurrent enqueue, stress scenarios
- **Crash Recovery:** Job persistence, state consistency across restarts
- **Deadline Enforcement:** Expiration handling, deadline before execution
- **Wake-Up Latency:** Signaling efficiency, queue-specific dispatch

### Test Infrastructure

- Real storage providers (PostgreSQL 16, SQL Server 2022, MongoDB 7, Redis 7)
- Docker via Testcontainers
- xUnit with full isolation per fixture
- `.NET 8 SDK` required

**Location:** `tests/NexJob.ReliabilityTests.Distributed/`

---

## Steps

### 1. Define Test Scenario

**Determine:**
- What critical scenario needs validation?
- What failure modes must be tested?
- How will crash/recovery be simulated?
- What concurrency constraints apply?

**Questions:**
- Does this involve deadline behavior?
- Does this involve retry exhaustion?
- Does this involve state persistence?
- Does this need to run against all providers?

### 2. Test Design

**For crash recovery:**
- Start dispatcher
- Enqueue jobs
- Simulate crash (kill process)
- Restart dispatcher
- Verify jobs resume correctly

**For concurrency:**
- Enqueue same job multiple times concurrently
- Verify no duplicates execute
- Verify retry logic respects concurrency

**For deadline:**
- Enqueue job with deadline in past
- Verify job never executes
- Verify status is Expired

**For retry:**
- Create job that fails N times
- Verify retries execute
- Verify dead-letter handler invoked after exhaustion

### 3. Implementation

**Use:** 02-execution-mode.md

**Output:**
- Test class (provider-specific or generic)
- Real storage (Docker container)
- Deterministic assertions
- Zero warnings

**Deliverables:**
1. Test file (tests/NexJob.ReliabilityTests.Distributed/...)
2. Works with all providers
3. Deterministic outcomes

---

## Test Structure (Pattern)

```csharp
[Collection("Reliability")]
public class RetryAndDeadLetterTests : IAsyncLifetime
{
    private readonly IStorageProvider _storage;
    private readonly Dispatcher _dispatcher;

    [Fact]
    public async Task FailedJob_AllRetriesExhausted_InvokesDeadLetterHandler()
    {
        // Given: Job that always fails, max 2 retries
        var job = new FailingJob { MaxRetries = 2 };
        var deadLetterInvoked = false;

        // When: Job fails all retries
        await _storage.EnqueueAsync(job);
        await _dispatcher.ProcessAsync(ct); // Attempt 1 (fails)
        await _dispatcher.ProcessAsync(ct); // Attempt 2 (fails)
        await _dispatcher.ProcessAsync(ct); // Attempt 3 (fails, exhausted)

        // Then: Dead-letter handler invoked
        deadLetterInvoked.Should().BeTrue();
    }

    public async Task InitializeAsync()
    {
        _storage = /* initialize with provider */;
        _dispatcher = new Dispatcher(_storage);
    }

    public async Task DisposeAsync()
    {
        await _dispatcher.DisposeAsync();
        await _storage.DisposeAsync();
    }
}
```

---

## Provider Coverage

### All providers must be tested

- **PostgreSQL 16**
- **SQL Server 2022**
- **MongoDB 7**
- **Redis 7**

**Run all providers:**
```bash
dotnet test tests/NexJob.ReliabilityTests.Distributed -c Release --verbosity normal
```

**Run single provider:**
```bash
dotnet test tests/NexJob.ReliabilityTests.Distributed -c Release \
  --filter "Category=Reliability.Distributed&ClassName~Postgres"
```

**Run single scenario across all:**
```bash
dotnet test tests/NexJob.ReliabilityTests.Distributed -c Release \
  --filter "Category=Reliability.Distributed&ClassName~RetryAndDeadLetter"
```

---

## Determinism Requirements

- [ ] No timing-dependent assertions
- [ ] No sleep-based waits (use events/channels)
- [ ] All state changes persisted and observable
- [ ] Same test run produces same result every time
- [ ] Works reliably in CI (no flakes)

---

## Crash Recovery Pattern

```csharp
// 1. Start dispatcher and enqueue
var dispatcher = new Dispatcher(storage);
await storage.EnqueueAsync(jobA);
await dispatcher.ProcessAsync(ct);

// 2. Simulate crash (stop dispatcher, kill process)
await dispatcher.StopAsync();
process.Kill();

// 3. Restart (fresh dispatcher instance)
var dispatcher2 = new Dispatcher(storage);
var status = await storage.GetJobStatusAsync(jobA.Id);

// 4. Verify state persisted correctly
status.Should().Be(JobStatus.Processing); // or Succeeded, depending on scenario)
```

---

## Concurrency Pattern

```csharp
// Enqueue same job multiple times concurrently
var tasks = Enumerable.Range(0, 10)
    .Select(_ => storage.EnqueueAsync(job))
    .ToList();

await Task.WhenAll(tasks);

// Dispatch and verify no duplicates
var executed = 0;
await dispatcher.ProcessAsync(ct);

executed.Should().Be(1); // Only one execution despite 10 enqueues
```

---

## Validation

**Checks:**
- [ ] Test deterministic? (no flakes, timing-stable)
- [ ] Real storage used? (not mocks)
- [ ] All providers tested?
- [ ] State persisted correctly?
- [ ] Assertions clear?
- [ ] Zero compiler warnings?
- [ ] StyleCop compliant?

---

## Output Requirements

**Test Code:**
- Production-quality
- Deterministic (runs consistently)
- Real storage providers
- Clear test name and scenario
- All providers covered

**Results:**
- All tests pass locally
- Zero warnings
- Ready to run in CI

---

## Exit Criteria

- [ ] Scenario is clearly defined
- [ ] Test is deterministic
- [ ] All providers tested
- [ ] All tests pass
- [ ] Zero compiler warnings
- [ ] Real behavior validated
- [ ] Ready to commit

---

## Common Reliability Scenarios

### Deadline Enforcement
**Test:** Job with deadline in past never executes
**Providers:** All
**Scenario:** Enqueue expired job, verify Expired status

### Retry Exhaustion
**Test:** Dead-letter handler invoked after max retries
**Providers:** All
**Scenario:** Failing job, exhaust retries, verify handler called

### Crash Recovery
**Test:** Job resumes after dispatcher crash
**Providers:** All (especially distributed)
**Scenario:** Enqueue, crash mid-processing, restart, verify resume

### Concurrent Dequeue
**Test:** No duplicate executions on concurrent enqueue
**Providers:** All
**Scenario:** Concurrent enqueue of same job, verify only one executes

### Wake-Up Efficiency
**Test:** Local enqueue triggers immediate dispatch
**Providers:** InMemory, Postgres, SqlServer, Mongo, Redis
**Scenario:** Measure latency from enqueue to execution start
