# NexJob.ReliabilityTests

End-to-end reliability scenarios that run the full pipeline (scheduler, dispatcher, executor, storage) with several workers, on InMemory and on real databases through Testcontainers.

## How it is organised

Each scenario is written once, in an abstract class. A thin subclass per provider says how to register its storage.

| Scenario class | What it proves | Providers |
|---|---|---|
| `ConcurrencyScenarios` | Several workers run each job exactly once; no job is lost under concurrent enqueue and high throughput; an empty queue runs cleanly | InMemory, PostgreSQL, SQL Server, Redis, MongoDB |
| `RetryAndDeadLetterScenarios` | A failed job is retried; the dead-letter handler runs once after the last attempt; a throwing handler does not stop the dispatcher | InMemory, PostgreSQL, SQL Server, Redis, MongoDB |
| `RecoveryScenarios` | Jobs persisted by one host run exactly once on the next host; a failed job stays failed and is not re-run; a stored continuation still runs after its parent | Databases only (InMemory loses its jobs when the host stops, by design) |
| `ContinuationScenarios` | A child created with `ContinueWithAsync` waits for its parent, runs once after the parent succeeds and never runs if the parent fails; chains run in order and fan-out runs each child once; an unknown parent runs nothing | InMemory, PostgreSQL, SQL Server, Redis, MongoDB |
| `ControlServiceScenarios` | Pausing a queue stops execution and resuming releases it; a failed job can be requeued and gets fresh attempts; a deleted job is gone and never runs, also when it is deleted while it runs; unknown ids are ignored | InMemory, PostgreSQL, SQL Server, Redis, MongoDB |
| `*DeadlineTests` | A job still waiting when its deadline passes is marked `Expired` (queue paused while the deadline elapses; the test waits until the dispatcher has logged that every queue is paused before it enqueues) | Databases |
| `OrphanScenarios` | A job left `Processing` by a node that died is requeued by the watcher and runs once, costing one attempt; one on its last attempt is failed and never runs; a job on its last attempt calls its dead-letter handler exactly once with an `OrphanedJobException`, one with attempts left does not, and a handler that throws stops nothing; a live job with a fresh heartbeat is never given back; the recurring lock of a dead node is taken over after its TTL | PostgreSQL, SQL Server, Redis, MongoDB |
| `MultiNodeScenarios` | Three nodes on one queue run every job exactly once and share the work; a node that stops and one that starts late lose nothing; two nodes with the same recurring job enqueue one occurrence per second; two orphan watchers do not give an orphan back twice | PostgreSQL, SQL Server, Redis, MongoDB |
| `SqlServerCrashTests` | A NexJob node runs in its own process (`NexJob.ReliabilityTests.Worker`) and is killed with `Process.Kill` while its job runs; another host recovers the job, which runs once | SQL Server (the others share the code path) |
| `LiveObjectOverloadScenarios` | `AddNexJobPostgres(NpgsqlDataSource)`, `AddNexJobRedis(IConnectionMultiplexer)` and `AddNexJobMongoDB(IMongoDatabase)` on a real database: a job runs, the runtime settings store reads and writes, and NexJob does not dispose what the application owns | PostgreSQL, Redis, MongoDB |
| `ConnectionCountScenarios` | The database server sees about one connection per worker under load (asked from its own system views), for one node and for three | PostgreSQL, SQL Server, Redis, MongoDB |
| `PoolScenarios` | With a connection pool smaller than the workers every job still finishes, with its first attempt | PostgreSQL, SQL Server |
| `SqlServerConnectionObjectTests` | The `SqlConnection` constructor: a closed connection works, an open one whose password is gone is refused with a clear message | SQL Server |
| `PostgresRecurringTests` | Two nodes do not enqueue the same recurring occurrence twice | PostgreSQL |

Tests count executions and wait for a condition with a timeout; they do not sleep for a fixed time. Each test that shares a database uses its own queue.

## What is not covered

- A real process crash on PostgreSQL, Redis and MongoDB: it is tested with a killed process on SQL Server only, and the others are covered by the orphan scenarios, which recreate the state a crash leaves.
- Connection counts for SQL Server run three nodes in one process, so they share one ADO.NET pool; the figures are an upper-bound guard, not a model of three processes.
- Authentication on Redis (the container has no password) and MongoDB through a live object beyond what the container requires.
- Wake-up latency. Wall-clock bounds are fragile on shared CI runners.

## Running

```bash
# InMemory only (seconds, no Docker)
dotnet test tests/NexJob.ReliabilityTests -c Release --filter "Category=Reliability.InMemory"

# Real databases (needs Docker)
dotnet test tests/NexJob.ReliabilityTests -c Release --filter "Category=Reliability.Distributed"

# One provider
dotnet test tests/NexJob.ReliabilityTests -c Release --filter "FullyQualifiedName~PostgresConcurrencyTests"
```

## In CI

| Tier | When | What |
|---|---|---|
| 1 | Every pull request (`ci.yml`, job `Reliability-InMemory`) | The InMemory scenarios |
| 2 | Pull requests that touch a storage provider, `src/NexJob/Internal/**` or this project (`reliability.yml`) | Concurrency, restart and continuation scenarios, one parallel job per database provider |
| 3 | Nightly at 04:00 UTC and on demand (`gh workflow run reliability.yml`) | Every scenario on every database provider |

The unit-test job still excludes this project (`FullyQualifiedName!~Reliability`).

## Adding a scenario

1. Add a `[Fact]` to the matching `*Scenarios` class, or a new abstract class.
2. Count executions with `ExecutionCounter` and wait with `WaitUntil` / `WaitForAllSucceeded`.
3. Use `BuildHost(..., queues: [uniqueQueue])` when tests share a database.
4. Add a subclass per provider (`Trait("Category", "Reliability.InMemory")` or `"Reliability.Distributed"`).
