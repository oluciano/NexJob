---
title: "NexJob Retries and Dead-Letter Handling: Overview"
sidebarTitle: "Retries and dead letter"
description: "How NexJob handles a job that fails: retries with backoff, dead-letter handlers when attempts run out, and forwarding of exhausted jobs. Start here to pick a strategy."
---

When a job fails, NexJob first retries it. When every attempt is spent, the job is marked `Failed`, its dead-letter handler runs, and any forwarder gets a copy. Three pages cover the three steps:

<div class="grid cards" markdown>
  -   [**Retries**](retries.md)

    The global policy, `[Retry]`, backoff, execution timeout, per-job attempt limit and failures that skip retries.

  -   [**Dead-letter handling**](dead-letter.md)

    `IDeadLetterHandler<TJob>`, jobs whose node died, and how long failed jobs are kept.

  -   [**Dead-letter forwarding**](dead-letter-forwarding.md)

    Copy an exhausted job to Kafka, RabbitMQ or any destination.

</div>

## Choosing the right strategy

Use the table below to pick the right approach for common failure scenarios:

| Scenario | Recommended approach |
|---|---|
| Transient network error (timeout, connection reset) | Retries with exponential backoff |
| External API rate limiting | Retries with longer `InitialDelay` and higher `MaxDelay` |
| Invalid input data | `[Retry(0)]` — dead-letter immediately |
| Business rule violation | `[Retry(0)]` — retry won't fix it |
| Database deadlock | Retries with a short `InitialDelay`, 2–3 attempts |
| Payment or legal operation failure | Dead-letter handler with alerting and compensation |

## Forwarding a dead-lettered job

This section moved to [Dead-letter forwarding](dead-letter-forwarding.md).

## Try it

The [`NexJob.Sample.Reliability`](https://github.com/oluciano/NexJob/tree/develop/samples/NexJob.Sample.Reliability) sample runs retries, checkpoint resume and a dead-letter handler on in-memory storage, with no infrastructure: `POST /retry/flaky` fails twice and succeeds on the third attempt, `POST /checkpoint` resumes a retried job from its last step, and `POST /deadletter` ends in the dead-letter handler. Open the dashboard at `/dashboard` while you call them.
