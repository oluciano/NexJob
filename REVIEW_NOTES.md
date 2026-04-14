# v2 Feature Review Notes

## v2.1 — JobRecordFactory + IScheduler non-generic
- [ OK ] `JobRecordFactory.Build` non-generic overload correctly implemented and documented.
- [ OK ] `IScheduler.EnqueueAsync(JobRecord)` added to interface and default implementation.
- [ OK ] OTel spans and metrics are consistent with generic overloads.
- [ OK ] Simple type name extraction for metrics works for assembly-qualified names.

## v2.2 — Azure Service Bus + AWS SQS
- [ OK ] Azure Service Bus trigger implements all 5 guarantees (at-least-once, lock renewal, explicit ack, dead-letter, graceful shutdown).
- [ OK ] AWS SQS trigger implements all 5 guarantees (visibility extension, explicit ack, DLQ via redrive, graceful shutdown).
- [ OK ] Both triggers use `IScheduler.EnqueueAsync(JobRecord)` correctly.
- [ ISSUE ] `src/NexJob.Trigger.AwsSqs/AwsSqsTrigger.cs` contains an obsolete architectural note in `<remarks>` (lines 14-20) stating it uses `IStorageProvider` directly.

## v2.3 — RabbitMQ + Kafka
- [ OK ] RabbitMQ trigger implements all 5 guarantees.
- [ OK ] RabbitMQ correctly uses `BasicNack(requeue: false)` on permanent failure and has a reconnect loop.
- [ OK ] Kafka trigger implements all 5 guarantees with manual offset commit.
- [ OK ] Kafka has `EnableAutoCommit = false` and manual commit after successful enqueue.
- [ OK ] Kafka DLT logic (produce then commit) correctly implemented.

## v2.4 — Google Pub/Sub + OpenTelemetry
- [ OK ] Google Pub/Sub trigger returns `Ack` only after successful enqueue and `Nack` on failure/cancellation.
- [ OK ] `NexJob.OpenTelemetry` package correctly exposes existing core `ActivitySource` and `Meter`.
- [ OK ] No modifications made to core `src/NexJob/` files for OTel exposure.

## Issues Found
- **File:** `src/NexJob.Trigger.AwsSqs/AwsSqsTrigger.cs` (lines 14-20)
  - **Issue:** Obsolete architectural note in `<remarks>` says the trigger uses `IStorageProvider` directly because `IScheduler` is generic. This was resolved in v2.2, and the implementation now correctly uses `IScheduler`. The comment should be removed.
