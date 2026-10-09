# NexJob.Redis

High-throughput Redis storage provider for **NexJob** — the background job scheduler for .NET 8+.

Backed by **StackExchange.Redis**, this provider evaluates server-side **Lua scripts** to execute atomic, lock-free state transitions and priority queue operations at microsecond latencies.

---

## Installation

```bash
dotnet add package NexJob.Redis
```

---

## Quick Start

Register Redis storage in your dependency injection container **before** calling `AddNexJob()`:

```csharp
using NexJob;
using NexJob.Redis;

var builder = WebApplication.CreateBuilder(args);

// 1. Register Redis storage
builder.Services.AddNexJobRedis("localhost:6379,abortConnect=false");

// 2. Register NexJob core services
builder.Services.AddNexJob(options =>
{
    options.Workers = 10;
    options.QueuePrefix = "myapp"; // the "default" queue below is stored as "myapp.default"
    options.Queues = ["default", "critical"];
});
```

Or provide a pre-configured `IConnectionMultiplexer`:

```csharp
var multiplexer = ConnectionMultiplexer.Connect("localhost:6379");
builder.Services.AddNexJobRedis(multiplexer);
```

---

## Distributed Throttling

`[Throttle]` limits how many jobs run **concurrently** per named resource. By default that limit applies per process; when you scale workers across several instances or containers you can enforce it cluster-wide with `AddNexJobDistributedThrottle`:

```csharp
builder.Services.AddNexJobRedis("localhost:6379")
    .AddNexJobDistributedThrottle();
```

Any job decorated with `[Throttle("external-api", maxConcurrent: 50)]` then runs at most 50 at a time across all worker nodes. Each running job holds an expiring entry that its node keeps refreshing, so slots left behind by a crashed node are reclaimed automatically (after three `HeartbeatInterval` periods); `DistributedThrottleTtl` caps how long one job may hold a slot. This is a concurrency limit, not a rate (per-minute) limit.

---

## High-Throughput Batch Processing

For heavy ingestion workloads, enable batch processing to leverage server-side Lua scripts and vectorized acknowledgments:

```csharp
builder.Services.AddNexJobRedis("localhost:6379,abortConnect=false");

builder.Services.AddNexJob(options =>
{
    // Workers dynamically batch fetches to keep all idle slots busy (FetchBatchScript in 1 RTT)
    options.Workers = 30;
    options.PollingInterval = TimeSpan.FromMilliseconds(20);

    // Commit successful jobs in asynchronous batches via AcknowledgeBatchScript
    options.EnableBatchAcknowledgment = true;
});
```

---

## Features
- **High-Throughput Batch Processing:** Atomic batch claims and single-roundtrip acknowledgments via `FetchBatchScript` and `AcknowledgeBatchScript`.

- **Microsecond Latency:** Ultra-fast job enqueue and dispatch leveraging in-memory data structures.
- **Server-Side Lua Scripts:** All complex state transitions (dequeuing, state commits, retry rescheduling) run atomically inside Redis, preventing race conditions.
- **Priority Queues:** Implemented using Redis Sorted Sets (`ZSET`) ordered by job priority and schedule timestamp.
- **Global Distributed Throttle:** Cluster-wide `[Throttle]` concurrency limits with crash-safe, expiring slots.
- **Job Index:** A sorted-set index of all jobs serves dashboard lists, metrics and retention without scanning the keyspace; jobs that already exist are indexed once, on first use.
- **Runtime Settings Store:** Hot-reloads queue concurrency and pause states from Redis hash keys.

---

## Key Structure

All keys are namespaced with `nexjob:` to avoid collisions:

| Key Pattern | Data Structure | Purpose |
|---|---|---|
| `nexjob:jobs:{id}` | Hash (`HSET`) | Job record properties and execution logs |
| `nexjob:queues:{queue}` | Sorted Set (`ZSET`) | Ready jobs sorted by priority and arrival time |
| `nexjob:scheduled` | Sorted Set (`ZSET`) | Delayed / scheduled jobs sorted by execution timestamp |
| `nexjob:processing` | Hash (`HSET`) | In-flight jobs being processed by workers |
| `nexjob:recurring:all` | Hash (`HSET`) | Recurring cron jobs and schedule configurations |
| `nexjob:servers:all` | Hash (`HSET`) | Active worker nodes and heartbeats |
| `nexjob:settings` | Hash (`HSET`) | Dynamic queue limits and pause status |

---

## Connection Configuration Example

```json
{
  "ConnectionStrings": {
    "Redis": "redis.production.cache.internal:6380,ssl=True,abortConnect=False,connectTimeout=5000"
  }
}
```
