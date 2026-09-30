# NexJob.ReliabilityTests.Distributed — Test Run Report

Real result of running the whole project once, after the fixture fixes of issue #258
(`dotnet test tests/NexJob.ReliabilityTests.Distributed -c Release`, Docker/Testcontainers, 2026-09-30).

```
Total:   201
Passed:  175
Failed:   26
Skipped:   0
```

No test is skipped; the failures below are left failing on purpose. This suite is **not run by any workflow** in
`.github/workflows/`.

## Result per class

| Class | Passed | Failed |
|---|---|---|
| MongoConcurrencyTests | 10 | 0 |
| MongoDeadlineTests | 6 | 4 |
| MongoRecoveryTests | 10 | 0 |
| MongoRetryAndDeadLetterTests | 9 | 1 |
| MongoWakeUpLatencyTests | 10 | 0 |
| PostgresConcurrencyTests | 10 | 0 |
| PostgresDeadlineTests | 6 | 4 |
| PostgresRecoveryTests | 10 | 0 |
| PostgresRecurringTests | 0 | 1 |
| PostgresRetryAndDeadLetterTests | 9 | 1 |
| PostgresWakeUpLatencyTests | 10 | 0 |
| RedisConcurrencyTests | 10 | 0 |
| RedisDeadlineTests | 6 | 4 |
| RedisRecoveryTests | 10 | 0 |
| RedisRetryAndDeadLetterTests | 9 | 1 |
| RedisWakeUpLatencyTests | 10 | 0 |
| SqlServerConcurrencyTests | 10 | 0 |
| SqlServerDeadlineTests | 6 | 4 |
| SqlServerRecoveryTests | 9 | 1 |
| SqlServerRetryAndDeadLetterTests | 8 | 2 |
| SqlServerWakeUpLatencyTests | 7 | 3 |

## What #258 fixed

- `FailOnceThenSucceedJob` / `FailOnceThenSucceedJobWithInput` counted attempts in an instance field of a transient job, so
  every attempt saw `_attempt == 1` and could never succeed. They now read `IJobContext.Attempt`. The
  `RetryExecutesCorrectlyAfterFailure_*` and `MultipleJobsWithDifferentRetryBehavior_*` tests pass on all four providers.
- `DeadLetterHandlerExceptionDoesNotCrashDispatcher_*` enqueued a `SuccessJob` / `SuccessJobWithInput` that was never
  registered in DI, so it could not succeed. The registration was added (assertions untouched).
- `DeadLetterHandlerInvokedAfterMaxAttemptsExhausted_WithInput` never called `RecordingDeadLetterHandler<...>.Reset()`
  (the `NoInput` variant does), so the static counter accumulated across provider classes.

## Remaining failures (not fixed here, recorded as follow-ups)

### 1. Deadline tests — 16 failures (4 per provider), test premise is invalid
`JobNotExecutedAfterDeadline_*` and `ExpirationRespectedEvenAfterRetries_*` expect the job to end `Expired`.
The job is enqueued with `deadlineAfter: 100 ms`, but the wake-up channel dispatches it within milliseconds, so it
**runs and succeeds before the deadline elapses**. Verified on `PostgresDeadlineTests.JobNotExecutedAfterDeadline_NoInput` (log: `SuccessJob executed` / `completed successfully`, attempt 1/3); the other 15 fail with the same message and are assumed to share the cause.
Message: `Expected job not to be <null> because job should be marked as Expired.`
These tests need a setup where the job is still waiting when the deadline passes (for example a busy worker or a paused queue).

### 2. `PostgresRecurringTests.MultipleNodes_RunningSameRecurringJob_OnlyOneEnqueuesPerOccurrence` — test bug
`System.InvalidOperationException : No service for type 'Microsoft.Extensions.Hosting.IHost' has been registered.`

### 3. `DeadLetterHandlerInvokedAfterMaxAttemptsExhausted_*` — 5 intermittent failures, shared static state
`Expected RecordingDeadLetterHandler<...>.LastFailedJob!.Id to be <id>`. `RecordingDeadLetterHandler<T>` keeps
`LastFailedJob` and `InvocationCount` in statics that the four provider classes share while xunit runs them in parallel.
Which tests fail changes from run to run (with parallelism disabled the `NoInput` variants pass).

### 4. SQL Server deadlocks — 5 failures, possibly a production issue
`SqlServerRecoveryTests.ConcurrentFailureRecoveryWithMultipleWorkers_WithInput` and three `SqlServerWakeUpLatencyTests`
fail with `Microsoft.Data.SqlClient.SqlException : Transaction (Process ID N) was deadlocked on lock resources with another
process and has been chosen as the deadlock victim.` under concurrent workers. Needs investigation in
`SqlServerStorageProvider` (retry on error 1205, or lock ordering).

The 2 `SqlServerRetryAndDeadLetterTests` failures have the same symptom as item 3 and are counted there.
