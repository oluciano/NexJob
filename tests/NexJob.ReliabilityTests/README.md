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
| `*DeadlineTests` | A job still waiting when its deadline passes is marked `Expired` (queue paused while the deadline elapses) | Databases |
| `PostgresRecurringTests` | Two nodes do not enqueue the same recurring occurrence twice | PostgreSQL |

Tests count executions and wait for a condition with a timeout; they do not sleep for a fixed time. Each test that shares a database uses its own queue.

## What is not covered

- A crash in the middle of an execution (orphan requeue). Stopping a host is graceful, so it cannot simulate one; orphan recovery has per-provider contract tests in `NexJob.IntegrationTests`.
- Wake-up latency. Wall-clock bounds are fragile on shared CI runners.
- Recurring jobs on providers other than PostgreSQL.

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
