---
title: "NexJob JobRecord Reference: Every Field Stored for a Job"
sidebarTitle: "JobRecord"
description: "Every field of the JobRecord NexJob stores for a job: identity, input, placement, timing, attempts, errors, progress and tracing. The record your dead-letter handlers and filters receive."
---

`JobRecord` is what NexJob stores for each job, and it is what you receive in a few places:

- an [`IDeadLetterHandler<TJob>`](../concepts/dead-letter.md) and an `IDeadLetterForwarder`, as `failedJob`;
- an [`IJobExecutionFilter`](../guides/job-filters.md), as `context.Job`;
- [`IScheduler.EnqueueAsync(JobRecord, ...)`](../concepts/scheduling.md), which enqueues a record that was built beforehand.

You normally never create one yourself: `EnqueueAsync<TJob, TInput>` builds it for you. Do not write to the fields marked as managed by NexJob.

## Identity and input

| Field | Type | Meaning |
|---|---|---|
| `Id` | `JobId` | Unique identifier of this job. |
| `JobType` | `string` | Assembly-qualified name of the job class, used to resolve it from the container. |
| `InputType` | `string` | Assembly-qualified name of the input type, used to deserialize `InputJson`. |
| `InputJson` | `string` | The input, serialized as JSON. Managed by NexJob. See [Job payloads](../concepts/job-payloads.md). |
| `SchemaVersion` | `int` | Version of the input shape. See [Job payloads](../concepts/job-payloads.md). |

## Placement

| Field | Type | Meaning |
|---|---|---|
| `Queue` | `string` | The [queue](../concepts/queues.md) the job belongs to. |
| `Priority` | `JobPriority` | `Critical`, `High`, `Normal` (default) or `Low`. See [Scheduling](../concepts/scheduling.md#priority). |
| `Status` | `JobStatus` | The current state. See the [state machine](../mental-model.md#job-state-machine). |
| `IdempotencyKey` | `string?` | The key used to deduplicate the job. See [Idempotency](../concepts/idempotency.md). |
| `Tags` | `IReadOnlyList<string>` | Labels set at enqueue time, used for dashboard filtering and `GetJobsByTagAsync`. |
| `ParentJobId` | `JobId?` | For a job created with `ContinueWithAsync`, the parent it waits for. See [Continuations](../concepts/continuations.md). |
| `RecurringJobId` | `string?` | The recurring schedule that created this job, or `null` for a job you enqueued. See [Recurring jobs](../concepts/recurring-jobs.md). |

## Timing

| Field | Type | Meaning |
|---|---|---|
| `CreatedAt` | `DateTimeOffset` | When the job was created (UTC). |
| `ScheduledAt` | `DateTimeOffset?` | When a scheduled job becomes eligible. `null` for a job enqueued for immediate execution. |
| `ExpiresAt` | `DateTimeOffset?` | The deadline: if the job has not started by then it is marked `Expired`. Calculated at enqueue time from `deadlineAfter`; `null` when none was set. |
| `ProcessingStartedAt` | `DateTimeOffset?` | When a worker last claimed the job. |
| `HeartbeatAt` | `DateTimeOffset?` | The last heartbeat of the node running the job. A job whose heartbeat is older than `HeartbeatTimeout` is treated as orphaned. See [Delivery guarantees](../concepts/delivery-guarantees.md). |
| `RetryAt` | `DateTimeOffset?` | When the job will next run after a failed attempt. |
| `CompletedAt` | `DateTimeOffset?` | When the job finished, whether it succeeded or failed for good. |

## Attempts and errors

| Field | Type | Meaning |
|---|---|---|
| `Attempts` | `int` | How many attempts have been made so far. |
| `MaxAttempts` | `int` | The limit for this job: the `maxAttempts` you passed, else `[Retry(n)]`, else `NexJobOptions.MaxAttempts`. See [Retries](../concepts/retries.md#per-job-attempt-limit). |
| `LastErrorMessage` | `string?` | The message of the last failed attempt. |
| `LastErrorStackTrace` | `string?` | The stack trace of the last failed attempt. |
| `ExecutionLogs` | `IReadOnlyList<JobExecutionLog>` | The log entries of the last execution, capped by `MaxJobLogLines`. |

## Progress and checkpoint

| Field | Type | Meaning |
|---|---|---|
| `ProgressPercent` | `int?` | Progress from 0 to 100, as reported by the job. `null` when not reported. |
| `ProgressMessage` | `string?` | The last progress message the job reported. |
| `CheckpointJson` | `string?` | The saved checkpoint of a long-running job, cleared when the job succeeds. See [Job context](../guides/job-context.md#progress-checkpoints-for-long-running-jobs). |

## Tracing

| Field | Type | Meaning |
|---|---|---|
| `TraceParent` | `string?` | The W3C `traceparent` captured when the job was enqueued, used to link the execution to the span that enqueued it. See [OpenTelemetry](../integrations/opentelemetry.md). |
