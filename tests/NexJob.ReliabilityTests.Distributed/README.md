# NexJob.ReliabilityTests.Distributed

Distributed reliability testing suite for NexJob. Validates all scenarios against **real storage providers** via Docker (Testcontainers).

## What's Tested

Every storage provider (Postgres, SQL Server, Redis, MongoDB) is tested against the following scenarios:

- **Retry & Dead-Letter** — Retry execution, handler invocation, exception resilience
- **Concurrency** — Duplicate prevention, concurrent enqueue, stress scenarios
- **Crash Recovery** — Job persistence, state consistency across restarts
- **Deadline Enforcement** — Expiration handling, enforcement before execution
- **Wake-Up Latency** — Signaling efficiency, queue-specific dispatch

## Both IJob and IJob<T> Variants

Every test scenario exists in two forms to ensure both job interfaces work correctly:
- `_NoInput` — uses `IJob` stubs (SuccessJob, AlwaysFailJob, etc.)
- `_WithInput` — uses `IJob<T>` stubs (SuccessJobWithInput, AlwaysFailJobWithInput, etc.)

This dual coverage ensures the entire dispatcher pipeline, serializer, and executor cache are validated for both interfaces.

## Provider Support

| Provider    | Status | Notes |
|-------------|--------|-------|
| PostgreSQL  | ✅     | Full coverage via pg_notify  |
| SQL Server  | ✅     | sp_getapplock contention  mitigated with workers=2 |
| Redis       | ✅     | TTL index async tolerance in deadline tests |
| MongoDB     | ✅     | TTL index async tolerance in deadline tests |

## Running Tests

### All tests
```bash
dotnet test tests/NexJob.ReliabilityTests.Distributed -c Release --verbosity normal
```

### Single provider
```bash
dotnet test tests/NexJob.ReliabilityTests.Distributed -c Release \
  --filter "Category=Reliability.Distributed&ClassName~Postgres"
```

### Single category across all providers
```bash
dotnet test tests/NexJob.ReliabilityTests.Distributed -c Release \
  --filter "Category=Reliability.Distributed&ClassName~Concurrency"
```

## Project Structure

```
DistributedReliabilityTestBase.cs      — Base class with host builder and utilities
SuccessJob.cs                          — IJob stubs (no input)
SuccessJobInput.cs                     — IJob<T> stubs (with input)
RecordingDeadLetterHandler.cs          — Dead-letter handler test fixtures
*ReliabilityFixture.cs                 — Testcontainers fixtures (4 providers)
Postgres*Tests.cs                      — Postgres test classes (5 categories)
SqlServer*Tests.cs                     — SQL Server test classes (5 categories)
Redis*Tests.cs                         — Redis test classes (5 categories)
Mongo*Tests.cs                         — MongoDB test classes (5 categories)
TEST_GENERATION_GUIDE.md               — Guide for adding remaining test classes
```

## Test Count

- **RetryAndDeadLetterTests** — 5 tests × 2 variants × 4 providers = 40 tests ✅
- **ConcurrencyTests** — 5 tests × 2 variants × 4 providers = 40 tests (1/4 providers)
- **RecoveryTests** — 5 tests × 2 variants × 4 providers = 40 tests (0/4 providers)
- **DeadlineTests** — 6 tests × 2 variants × 4 providers = 48 tests (1/4 providers)
- **WakeUpLatencyTests** — 4 tests × 2 variants × 4 providers = 32 tests (0/4 providers)

**Total: ~200 tests when all categories are complete**

## Adding New Test Classes

See `TEST_GENERATION_GUIDE.md` for:
- Test pattern for each category
- Provider-specific adjustments
- Both IJob and IJob<T> variant pattern
- Code generation strategy

## Requirements

- Docker (Testcontainers manages container lifecycle)
- .NET 8 SDK
- 0 warnings in Release builds (TreatWarningsAsErrors=true)

## Zero-Warnings Requirement

All code compiles without warnings in Release mode. StyleCop compliance is enforced at build time.

## Notes

- Each test class is fully isolated via `IClassFixture<ProviderFixture>`
- Containers are created/destroyed per test class (not per test method)
- Static state (ExecutionCount, LastFailedJob, etc.) is reset via `ResetTestState()`
- All job types are `sealed` per architecture guidelines
- CancellationToken is propagated throughout all async chains
