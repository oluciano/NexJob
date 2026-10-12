---
title: "NexJob Redis Storage: Sorted Sets, Lua Scripts and Distributed Throttle"
sidebarTitle: "Redis"
description: "Use NexJob.Redis to store NexJob jobs in Redis: sorted sets, atomic Lua scripts, a job index, batch acknowledgment and cluster-wide throttling. Tried on standalone Redis; Redis Cluster is rejected at startup."
---

NexJob's Redis provider stores jobs in sorted sets, uses server-side Lua scripts for atomic state transitions and for fetching and acknowledging jobs in batches, and keeps a sorted-set job index so dashboard queries do not scan the whole keyspace. Choose Redis for ephemeral high-frequency jobs or for cluster-wide rate limiting across worker nodes. It was tried on a standalone Redis and does **not** work on Redis Cluster, which `AddNexJobRedis` rejects with an error; see below.

## Install

```bash
dotnet add package NexJob.Redis
```

## Basic setup

Register the provider **before** `AddNexJob()`. Pass a `StackExchange.Redis`-compatible connection string.

```csharp
using NexJob.Redis;

// 1. Register Redis storage
builder.Services.AddNexJobRedis("localhost:6379,abortConnect=false");

// 2. Register NexJob core services
builder.Services.AddNexJob(options =>
{
    options.Workers = 10;
    options.QueuePrefix = "myapp"; // the "default" queue below is stored as "myapp.default"
    options.Queues  = ["default", "critical"];
});
```

## Register via an existing multiplexer

If your application already manages a `ConnectionMultiplexer` — for example, for caching — pass it directly so NexJob shares the same connection:

```csharp
using StackExchange.Redis;
using NexJob.Redis;

// Reuse your application's existing multiplexer
IConnectionMultiplexer mux = ConnectionMultiplexer.Connect("localhost:6379");
builder.Services.AddSingleton(mux);

builder.Services.AddNexJobRedis(mux);
builder.Services.AddNexJob();
```

!!! note
    Redis uses a single shared multiplexer (one per process), not a pool of connections. There is no pool size to configure — StackExchange.Redis multiplexes all commands over a small number of persistent sockets automatically.


## Use cases

Redis is a good fit when:

- **Jobs are ephemeral.** Short-lived, fire-and-forget jobs that do not need long-term history or relational queries fit Redis's in-memory model.
- **You need cluster-wide throttling.** The `[Throttle]` attribute, backed by the Redis distributed throttle store, enforces limits across every worker node.
- **You process high-frequency event streams.** Fetching a worker-capacity batch and acknowledging completions each take one script call, which suits Kafka or RabbitMQ ingestion pipelines.

Redis does not change how fast a job starts on the node that enqueued it (that goes through the in-process wake-up channel on every provider), or how long a job enqueued by another node waits (up to the `PollingInterval`). No storage-latency measurement is published for the providers, so compare them on your own workload.

## Redis Cluster is not supported

`AddNexJobRedis` throws an `InvalidOperationException` when the connection reaches a Redis Cluster (any server that reports cluster mode, including a single-shard cluster), before any job is touched. The Lua scripts touch keys they do not declare, and a cluster rejects that (`ERR Script attempted to access a non local key in a cluster node`); without the check, no job ran on a 3-node cluster while the same program on a standalone Redis ran all of them. Use a standalone Redis. Tracked in [#479](https://github.com/oluciano/NexJob/issues/479).

## A primary with a replica

Tried with Redis 7: one primary and one replica, not in cluster mode (`Category=Reliability.Validation` in `NexJob.ReliabilityTests`).

- **Connect to the primary, or to both.** With both endpoints in the connection string, in either order, jobs run once each: NexJob sends every write and script to the primary, and the replica only keeps a copy.
- **Do not connect to the replica alone.** The host starts, but every enqueue fails with `READONLY You can't write against a read only replica`.
- **Losing the primary.** With both endpoints configured, after the primary stopped and an operator promoted the replica (`REPLICAOF NO ONE`), NexJob kept the jobs that had been replicated and ran a new job, without a restart of the host. NexJob does not promote a replica itself.

Not tried: Redis Sentinel, a managed failover, a job that was `Processing` when the primary was lost, and writes the primary had not yet sent to the replica (replication is asynchronous, so a failover can lose the last ones).

## Distributed throttling

Enable the `AddNexJobDistributedThrottle()` extension to enforce `[Throttle]` limits globally across all worker nodes, not just within a single process. Each running throttled job holds an expiring Redis entry that its worker node keeps refreshing. When a node crashes, its entries expire automatically and the slots are reclaimed without manual intervention.

```csharp
builder.Services.AddNexJobRedis("localhost:6379")
    .AddNexJobDistributedThrottle();

builder.Services.AddNexJob();
```

Without `AddNexJobDistributedThrottle()`, `[Throttle]` limits are enforced per-process only.

## High-throughput batch processing

For extreme ingestion workloads — for example, consuming tens of thousands of messages from Kafka or RabbitMQ — enable batch processing. Workers claim a full capacity-batch of jobs in a single server-side Lua script round trip and acknowledge completions with a single script call for the whole batch.

```csharp
builder.Services.AddNexJobRedis("localhost:6379,abortConnect=false");

builder.Services.AddNexJob(options =>
{
    // Workers dynamically batch-fetch to keep all idle slots busy in a single round trip
    options.Workers         = 30;
    options.PollingInterval = TimeSpan.FromMilliseconds(20);

    // Commit successful jobs in asynchronous batches with one Lua call (off by default)
    options.EnableBatchAcknowledgment = true;
});
```

## Job index reconciliation

NexJob maintains a sorted-set job index alongside the main job keys. This index powers dashboard lists, metrics, and retention without scanning the entire keyspace. The index is updated atomically on every job state transition. NexJob also rebuilds it from the job hashes at most once an hour, by one node at a time, so a drift (for example after a manual Redis operation, or a node on an older version that does not write the index) is repaired without a restart.

## Connection string format

The connection string follows `StackExchange.Redis` configuration syntax. Common options:

| Option | Example value | Purpose |
|---|---|---|
| Host and port | `localhost:6379` | Connect to a standalone Redis instance |
| `abortConnect=false` | `localhost:6379,abortConnect=false` | Retry connection on startup instead of throwing |
| Password | `localhost:6379,password=secret` | Authenticate with Redis AUTH |
| SSL | `redis.example.com:6380,ssl=true` | TLS-encrypted connection |

!!! warning
    Redis is an in-memory store by default. If your Redis instance restarts without persistence configured, **all pending and queued jobs are lost**. For production use, enable either RDB snapshots or AOF (append-only file) persistence in your Redis configuration (`save` or `appendonly yes`). Verify that your Redis persistence settings match your tolerance for data loss.

