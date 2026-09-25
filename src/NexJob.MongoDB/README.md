# NexJob.MongoDB

MongoDB storage provider for **NexJob** — the background job scheduler for .NET 8+.

Backed by the official **MongoDB.Driver**, this provider uses `FindOneAndUpdateAsync` with atomic condition filters for concurrency-safe, lock-free job claiming across distributed workers.

---

## Installation

```bash
dotnet add package NexJob.MongoDB
```

---

## Quick Start

Register MongoDB storage in your dependency injection container **before** calling `AddNexJob()`:

```csharp
using NexJob;
using NexJob.MongoDB;

var builder = WebApplication.CreateBuilder(args);

// 1. Register MongoDB storage
builder.Services.AddNexJobMongoDB(
    connectionString: builder.Configuration.GetConnectionString("MongoConnection")!,
    databaseName: "nexjob");

// 2. Register NexJob core services
builder.Services.AddNexJob(options =>
{
    options.Workers = 10;
    options.Queues = ["default", "critical"];
});
```

Or provide a pre-configured `IMongoDatabase`:

```csharp
var client = new MongoClient("mongodb://localhost:27017");
var database = client.GetDatabase("nexjob");

builder.Services.AddNexJobMongoDB(database);
```

---

## High-Throughput Batch Processing

For heavy ingestion workloads, enable batch processing to group job reservations and vectorized acknowledgments using `UpdateManyAsync`:

```csharp
builder.Services.AddNexJobMongoDB(
    connectionString: builder.Configuration.GetConnectionString("MongoConnection")!,
    databaseName: "nexjob");

builder.Services.AddNexJob(options =>
{
    // Workers dynamically batch fetches to keep all idle slots busy
    options.Workers = 30;
    options.PollingInterval = TimeSpan.FromMilliseconds(20);

    // Commit successful jobs in asynchronous batches via UpdateManyAsync ($in: [ids])
    options.EnableBatchAcknowledgment = true;
});
```

---

## Features

- **High-Throughput Batching:** Atomic batch claim and vectorized batch acknowledgment (`UpdateManyAsync` by ID set).
- **Atomic State Transitions:** Uses MongoDB `FindOneAndUpdate` with optimistic filter criteria (`status: Enqueued`) to prevent race conditions across multiple nodes without global locks.
- **Automatic Index Creation:** Automatically builds compound and unique indexes on initialization for queues, priority, idempotency keys, and scheduled timestamps.
- **Document-Oriented Storage:** Flexible document schema for job arguments, execution metadata, and logs.
- **Distributed Recurring Locks:** Manages cron recurring job execution safely across replica sets using atomic collection locks (`nexjob_recurring_locks`).
- **Runtime Settings Store:** Persists dynamic queue limits and pause status in MongoDB.

---

## Collections

The provider automatically manages the following collections in the target database:

| Collection | Purpose |
|---|---|
| `nexjob_jobs` | Job documents, input parameters, state transitions, retry attempts, and logs |
| `nexjob_recurring_jobs` | Recurring cron job schedules and execution tracking |
| `nexjob_recurring_locks` | Distributed locks for recurring job scheduling |
| `nexjob_servers` | Worker node heartbeats and cluster discovery |
| `nexjob_settings` | Dynamic queue configurations and runtime controls |

---

## Connection String Example

```json
{
  "ConnectionStrings": {
    "MongoConnection": "mongodb://user:secret@localhost:27017/?authSource=admin&replicaSet=rs0"
  }
}
```
