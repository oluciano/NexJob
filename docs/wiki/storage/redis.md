---
title: "NexJob Redis Storage: Ultra-Low Latency Job Dispatch"
sidebarTitle: "Redis"
description: "Use NexJob.Redis to store NexJob jobs in Redis for ultra-low latency scheduling with atomic Lua scripts, a job index, and distributed throttle support."
---

NexJob's Redis provider delivers the lowest dispatch latency of all available backends. It stores jobs in sorted sets for priority ordering, uses server-side Lua scripts for atomic state transitions and vectorized batch operations, and maintains a dedicated sorted-set job index so dashboard queries never have to scan the full keyspace. Choose Redis when your workload demands sub-millisecond scheduling, ephemeral high-frequency jobs, or cluster-wide rate limiting across multiple worker nodes.

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

Redis is the right choice when:

- **Latency matters most.** Redis dispatches jobs in microseconds. If your application needs near-real-time job pickup with minimal scheduling overhead, Redis outperforms relational backends.
- **Jobs are ephemeral.** Short-lived, fire-and-forget jobs that do not require long-term persistence or relational queries are a natural fit for Redis's in-memory model.
- **You need cluster-wide throttling.** The `[Throttle]` attribute, backed by the Redis distributed throttle store, enforces rate limits across every worker node and container in your cluster.
- **You process high-frequency event streams.** Batch fetching via server-side Lua scripts allows a single round trip to claim a full worker-capacity batch, making Redis excellent for Kafka or RabbitMQ ingestion pipelines.

## Distributed throttling

Enable the `AddNexJobDistributedThrottle()` extension to enforce `[Throttle]` limits globally across all worker nodes, not just within a single process. Each running throttled job holds an expiring Redis entry that its worker node keeps refreshing. When a node crashes, its entries expire automatically and the slots are reclaimed without manual intervention.

```csharp
builder.Services.AddNexJobRedis("localhost:6379")
    .AddNexJobDistributedThrottle();

builder.Services.AddNexJob();
```

Without `AddNexJobDistributedThrottle()`, `[Throttle]` limits are enforced per-process only.

## High-throughput batch processing

For extreme ingestion workloads — for example, consuming tens of thousands of messages from Kafka or RabbitMQ — enable batch processing. Workers claim a full capacity-batch of jobs in a single server-side Lua script round trip and acknowledge completions in a single vectorized batch call.

```csharp
builder.Services.AddNexJobRedis("localhost:6379,abortConnect=false");

builder.Services.AddNexJob(options =>
{
    // Workers dynamically batch-fetch to keep all idle slots busy in a single round trip
    options.Workers         = 30;
    options.PollingInterval = TimeSpan.FromMilliseconds(20);

    // Commit successful jobs in asynchronous batches via a vectorized Lua acknowledgment script
    options.EnableBatchAcknowledgment = true;
});
```

## Job index reconciliation

NexJob maintains a sorted-set job index alongside the main job keys. This index powers dashboard lists, metrics, and retention without scanning the entire keyspace. The index is updated atomically on every job state transition; if it ever drifts out of sync (for example after a manual Redis operation), NexJob reconciles it automatically on the next startup.

## Connection string format

The connection string follows `StackExchange.Redis` configuration syntax. Common options:

| Option | Example value | Purpose |
|---|---|---|
| Host and port | `localhost:6379` | Connect to a standalone Redis instance |
| `abortConnect=false` | `localhost:6379,abortConnect=false` | Retry connection on startup instead of throwing |
| Password | `localhost:6379,password=secret` | Authenticate with Redis AUTH |
| SSL | `redis.example.com:6380,ssl=true` | TLS-encrypted connection |
| Cluster | `node1:6379,node2:6379,node3:6379` | Connect to a Redis Cluster |

!!! warning
    Redis is an in-memory store by default. If your Redis instance restarts without persistence configured, **all pending and queued jobs are lost**. For production use, enable either RDB snapshots or AOF (append-only file) persistence in your Redis configuration (`save` or `appendonly yes`). Verify that your Redis persistence settings match your tolerance for data loss.

