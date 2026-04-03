[Human Reference Only] - Not loaded by AI execution engine. For human planning only.

# Workflow: Test Implementation

**When:** Adding tests for existing code or new scenarios

---

## Entry Criteria

- Scenario to test is clearly described
- Expected behavior defined
- Test scope bounded

---

## Steps

### 1. Define Test Scope

**Determine:**
- What behavior is being tested?
- What are the preconditions?
- What is the expected outcome?
- What edge cases exist?

**Test types:**
- **Unit:** Isolated logic (one class/method)
- **Integration:** With real storage/DI
- **Reliability:** Crash recovery, concurrency, deadline enforcement

### 2. Test Design

**Before writing code, specify:**
- Test name (describes the scenario)
- Preconditions (Given)
- Action (When)
- Assertion (Then)
- Edge cases

**Example:**
```
Test: ExpiredJobDoesNotExecute
Given: Job with deadline in the past
When: Dispatcher fetches the job
Then: Job is marked Expired, handler never invoked
```

### 3. Implementation (Execution Mode)

**Use:** 02-execution-mode.md

**Output:**
- Test code (real behavior, no mocks)
- Passes locally
- Zero warnings

**Deliverables:**
1. Test file (tests/NexJob.Tests/... or appropriate location)
2. All tests pass
3. Zero compiler warnings

---

## Test Requirements

### Real Behavior

- Use real storage when possible (not mocks)
- Avoid test doubles unless necessary
- Integration tests with real database/cache

### Deterministic

- No timing-dependent assertions
- No race conditions
- All runs produce same result

### Coverage

- Happy path covered
- Error cases covered
- Edge cases covered
- Timeout scenarios (if applicable)

### Reliability Tests

For critical scenarios (deadline, retry, crash recovery):
- Test against real storage providers
- Use `NexJob.ReliabilityTests.Distributed` pattern
- Cover all providers (PostgreSQL, SQL Server, MongoDB, Redis)

---

## Test Structure (Pattern)

```csharp
public class FeatureNameTests
{
    [Fact]
    public async Task ScenarioDescription_Should_ProduceExpectedResult()
    {
        // Given: Set up preconditions
        var storage = new InMemoryStorage();
        var dispatcher = new Dispatcher(storage);
        var job = new TestJob();

        // When: Execute action
        await storage.EnqueueAsync(job);
        await dispatcher.ProcessAsync(cancellationToken);

        // Then: Assert expected behavior
        var result = await storage.GetJobStatusAsync(job.Id);
        result.Should().Be(JobStatus.Succeeded);
    }
}
```

---

## Test Naming Convention

**Format:** `{Scenario}_{Action}_{ExpectedResult}`

**Examples:**
- `ExpiredJob_Executed_NeverRuns`
- `FailedJob_AllRetriesExhausted_MovesToDeadLetter`
- `LocalEnqueue_WakeUpSignal_ImmediateExecution`
- `DeadLetterHandler_Throws_ExceptionSwallowed`

---

## Validation (Validation Mode)

**Use:** 03-validation-mode.md

**Checks:**
- [ ] Tests are deterministic?
- [ ] Real behavior (no inappropriate mocks)?
- [ ] Coverage complete?
- [ ] All tests pass?
- [ ] Zero compiler warnings?
- [ ] StyleCop compliant?

---

## Output Requirements

**Test Code:**
- Production-quality (could ship as example)
- Deterministic
- Clear test name
- Minimal setup
- Real behavior

**Documentation:**
- Clear assertions
- Comments explaining complex setup
- No explanation of test framework

---

## Test Categories

### Unit Tests
**Location:** `tests/NexJob.Tests/`
**Use:** Isolated logic, job handlers, attributes
**Storage:** InMemoryStorage

### Integration Tests
**Location:** `tests/NexJob.IntegrationTests/`
**Use:** Full workflow with real storage
**Storage:** Real database/cache (Docker via Testcontainers)

### Reliability Tests
**Location:** `tests/NexJob.ReliabilityTests.Distributed/`
**Use:** Critical scenarios across all providers
**Scenarios:**
- Retry & Dead-Letter
- Concurrency & Duplicate Prevention
- Crash Recovery & State Consistency
- Deadline Enforcement
- Wake-Up Latency

---

## Reliability Test Requirements (for complex scenarios)

- Test must run against all providers (PostgreSQL, SQL Server, MongoDB, Redis)
- Must validate crash recovery (job state survives restart)
- Must validate concurrency (no duplicates on concurrent enqueue)
- Must validate deadline (expired jobs never execute)
- Must validate retry (exhausted retries invoke dead-letter)

**Command to run:**
```bash
dotnet test tests/NexJob.ReliabilityTests.Distributed -c Release
```

---

## Exit Criteria

- [ ] Test scenario is clear
- [ ] Test code is production-ready
- [ ] All tests pass
- [ ] Zero compiler warnings
- [ ] StyleCop compliant
- [ ] Real behavior tested (not mocks)
- [ ] Coverage complete
- [ ] Ready to commit
