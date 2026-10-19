---
title: "NexJob Retries and Dead-Letter Handling: Overview"
sidebarTitle: "Retries and dead letter"
description: "How NexJob handles a job that fails: retries with backoff, dead-letter handlers when attempts run out, and forwarding of exhausted jobs. Start here to pick a strategy."
---

When a job fails, NexJob first retries it. When every attempt is spent, the job is marked `Failed`, its dead-letter handler runs, and any forwarder gets a copy. Each step has one page:

- [Retries](retries.md): global policy, `[Retry]`, backoff, execution timeout, per-job attempt limit, failures that skip retries, and which strategy fits which failure.
- [Dead-letter handling](dead-letter.md): `IDeadLetterHandler<TJob>`, jobs whose node died, and how long failed jobs are kept.
- [Dead-letter forwarding](dead-letter-forwarding.md): copy an exhausted job to Kafka, RabbitMQ or any destination.
