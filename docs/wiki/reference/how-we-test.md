---
title: "How NexJob is Tested: Real Databases, Crashes and Upgrades"
sidebarTitle: "How We Test"
description: "What NexJob's test suite proves and how it runs: contract tests on every storage provider, reliability scenarios with several nodes, a killed process, upgrade tests and what is not covered."
---

A job library is only as good as what it does on the worst day. NexJob tests the behaviour that matters on that day against real databases, not only against mocks. This page says what is covered, how it runs and what is not covered, so you can decide how much to trust it.

## The layers

| Layer | What it proves | Runs |
| --- | --- | --- |
| **Unit tests** | Retry policy, dead-letter dispatch, filters, scheduler, options, dashboard rendering and the trigger handlers, with fake storage and brokers | Every pull request |
| **Storage contract tests** | The same set of tests on **InMemory, PostgreSQL, SQL Server, Redis and MongoDB**: enqueue, fetch, commit, retries, orphan recovery, deduplication, recurring locks, continuations | Every pull request (real databases in containers) |
| **Broker integration tests** | Kafka, RabbitMQ, AWS SQS, Salesforce and the Google Pub/Sub emulator: a message becomes a job and is acknowledged, a failed enqueue is not acknowledged | Every pull request |
| **Reliability scenarios** | The whole pipeline (scheduler, dispatcher, executor, storage) with several workers and nodes, on the four databases. See below | A focused subset on pull requests that touch storage or the pipeline, everything every night |
| **Stress and load** | Sustained throughput and many workers on PostgreSQL and Redis | On demand, and before a release |
| **Dashboard gate** | A headless browser walks every dashboard route, mobile layouts, and feeds the dashboard hostile input; zero server errors allowed | Before a release |
| **Samples** | The runnable samples start and their endpoints behave as documented | Every pull request |

## Reliability scenarios

Each scenario is written once and runs on every database. They check, for example:

- **Exactly once under concurrency:** several workers and several nodes run each job once, and no job is lost under concurrent enqueue.
- **Retry and dead-letter:** a failed job is retried, the handler runs once after the last attempt, and a handler that throws does not stop the dispatcher.
- **Crash recovery:** a job left `Processing` by a node that died is recovered and runs once; one on its last attempt fails and its dead-letter handler runs once. On SQL Server a **node runs in its own process and is killed** while it runs a job.
- **Restarts:** jobs stored by one host run exactly once on the next host.
- **Continuations, deadlines, pausing, requeue and delete** behave as documented, including deleting a job while it runs.
- **Connections:** the database sees about one connection per worker under load, and a pool smaller than the workers still finishes every job.
- **Live objects:** `AddNexJobPostgres(NpgsqlDataSource)`, `AddNexJobRedis(IConnectionMultiplexer)` and `AddNexJobMongoDB(IMongoDatabase)` work on a real database.
- **Upgrades:** a storage filled by the previous version opens with the new one on every database, with its history, orphans and continuations intact, and the MongoDB case of old and new nodes sharing a database is covered.

Tests count executions and wait for a condition with a timeout; they do not sleep for a fixed time, and each test that shares a database uses its own queue.

## When it runs

- **Pull requests** run the unit, contract, broker and sample tests, plus the reliability scenarios that match the files changed.
- **Every night (04:00 UTC)** the full reliability suite runs on every database. A release waits for green nights.
- **Before a release** the stress run and the dashboard gate run, and the packages are installed from a local feed into an empty application and exercised on every provider.

## What is not covered

Being clear about gaps matters more than a long list of passing tests:

- A real **process kill** is tested on SQL Server. On PostgreSQL, Redis and MongoDB the orphan scenarios recreate the state a crash leaves, but do not kill a process.
- Connection counts for SQL Server run three nodes in one process, so they share one ADO.NET pool: the figures are an upper-bound guard, not a model of three processes.
- Authentication on Redis, and MongoDB through a live object beyond what the test container requires.
- **Wake-up latency.** Wall-clock bounds are too fragile on shared build machines to assert.
- Brokers other than Kafka, RabbitMQ, SQS, Salesforce and the Pub/Sub emulator are exercised by unit tests only.

## See also

- [Delivery Guarantees](../concepts/delivery-guarantees.md): the behaviour these tests check.
- [Migration](../reference/migration.md): what changes between versions and what the upgrade tests cover.
- The scenario list and the commands to run it yourself live in the repository: `tests/NexJob.ReliabilityTests/README.md`.
