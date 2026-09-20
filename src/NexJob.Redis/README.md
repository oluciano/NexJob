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

When scaling workers across multiple server instances or containers, in-memory rate limits only throttle per process. You can enable global, cluster-wide rate limiting with `AddNexJobDistributedThrottle`:

```csharp
builder.Services.AddNexJobRedis("localhost:6379")
    .AddNexJobDistributedThrottle();
```

Any job decorated with `[Throttle("external-api", 50, ThrottleWindow.PerMinute)]` will automatically enforce a strict global Redis rate limit across all worker nodes.

---

## Features

- **Microsecond Latency:** Ultra-fast job enqueue and dispatch leveraging in-memory data structures.
- **Server-Side Lua Scripts:** All complex state transitions (dequeuing, state commits, retry rescheduling) run atomically inside Redis, preventing race conditions.
- **Priority Queues:** Implemented using Redis Sorted Sets (`ZSET`) ordered by job priority and schedule timestamp.
- **Global Distributed Throttle:** Native sliding window rate limiting across multiple hosts.
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
