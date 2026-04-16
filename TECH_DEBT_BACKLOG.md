# NexJob Technical Debt & Rule Improvements Backlog

This backlog records findings during the **Reliability Hardening Phase**. These are items that require changes to **Business Logic** or **Architecture** to improve reliability and usability, which were not modified during the hardening of tests.

## Triggers

### TD001: AWS SQS Integration Test Flakiness (Regression Risk)
- **Component:** `NexJob.Trigger.AwsSqs`
- **Issue:** Integration test `EnqueueFailure_MessageNotDeleted` frequently fails to receive the message back from LocalStack after a simulated enqueue failure.
- **Finding:** Unit tests confirm `Delete` is not called, but the message may be stuck in a visibility extension loop or have an incorrect visibility timeout during error handling.
- **Impact:** Messages might take too long to be retried or get lost in specific failure modes.
- **Action:** Review `ExtendVisibilityAsync` and visibility timeout logic in `AwsSqsTrigger.cs`.

### ~~TD002: Kafka Missing Header Error Handling~~ ✅ RESOLVED
- **Component:** `NexJob.Trigger.Kafka`
- **Fixed in:** `KafkaTriggerHandler.ProcessMessageAsync` — `ExtractJobType` and `JobRecordFactory.Build` moved inside the try/catch block.
- **Tests:** `KafkaFutureHardeningTests.Kafka_MessageWithoutJobType_ShouldBeMovedToDeadLetter` (DLT path) and `Kafka_MessageWithoutJobType_NoDlt_DoesNotCommit_DoesNotThrow` (no-DLT path).

### TD003: Broker Trigger Input Type Generalization
- **Component:** All Trigger Packages
- **Issue:** All triggers currently hardcode `inputType` to `string.FullName`.
- **Finding:** Users have to implement `IJob<string>` and manually deserialize the body.
- **Impact:** Verbose boilerplate for end-users.
- **Action:** Allow `inputType` to be configured in Trigger Options so the `JobRecordFactory` can build records with the correct type, allowing users to implement `IJob<MyModel>` directly.

## Core

### TD004: JobExecutor Testability (Design)
- **Component:** `NexJob.Internal.JobExecutor`
- **Issue:** The class is `sealed internal` without an interface.
- **Finding:** Testing `JobDispatcherService` requires instantiating a real `JobExecutor` with all its dependencies mocked.
- **Impact:** Maintenance of dispatcher tests is harder.
- **Action:** Consider extracting `IJobExecutor` to allow cleaner mocking in the dispatcher loop tests.

### TD005: DefaultJobRetryPolicy Jitter Precision
- **Component:** `NexJob.Internal.DefaultJobRetryPolicy`
- **Issue:** Jitter is calculated but its impact on very short delays (ms) might be negligible or cause overlaps.
- **Action:** Audit jitter implementation for edge cases with high-concurrency short-delay retries.
