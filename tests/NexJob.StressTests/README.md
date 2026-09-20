# NexJob Production Stress & Load Tests

High-throughput load, concurrency, and backpressure stress tests for **NexJob** storage providers and triggers.

---

## Test Suites

| Suite | Target | Concurrency | Focus |
|---|---|---|---|
| [`PostgresStorageStressTests`](PostgresStorageStressTests.cs) | PostgreSQL 16 (`NpgsqlDataSource`) | 20 workers, 20 parallel producers | 3,000 jobs under lock contention. Asserts zero deadlocks (`40P01`), connection pool stability, and 100% completion. |
| [`RedisStorageStressTests`](RedisStorageStressTests.cs) | Redis 7 | 20 workers, 20 parallel producers | 3,000 jobs under Redis multiplexer load. Asserts zero connection drops and consistent status transitions. |
| [`TriggerBackpressureStressTests`](TriggerBackpressureStressTests.cs) | Broker & Triggers | 50 in-flight concurrent | 5,000 messages under artificial storage backpressure. Asserts bounded memory (< 100 MB growth) and prefetch enforcement. |

---

## Running Stress Tests Locally

### Option 1: Automatic via Testcontainers (Docker must be running)
```bash
dotnet test tests/NexJob.StressTests/NexJob.StressTests.csproj -c Release
```

### Option 2: Against Pre-started Infrastructure (`samples/docker-compose.yml`)
```bash
# 1. Start local stack
docker compose -f samples/docker-compose.yml up -d

# 2. Run stress tests pointing to local services
NEXJOB_STRESS_POSTGRES="Host=localhost;Port=5432;Username=postgres;Password=postgres;Database=postgres" \
NEXJOB_STRESS_REDIS="localhost:6379" \
dotnet test tests/NexJob.StressTests/NexJob.StressTests.csproj -c Release
```

---

## CI Integration

Stress tests are tagged with `[Trait("Category", "Stress")]` and excluded from the fast Pull Request CI pipeline to maintain swift turnaround times.

They can be triggered on-demand via the **GitHub Actions** manual workflow:
`.github/workflows/stress-tests.yml`
