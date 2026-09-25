# NexJob.Postgres

PostgreSQL storage provider for **NexJob** — the background job scheduler for .NET 8+.

Backed by **Npgsql** and **Dapper**, this provider uses `SELECT FOR UPDATE SKIP LOCKED` for atomic, concurrency-safe job fetching across multiple distributed worker nodes.

---

## Installation

```bash
dotnet add package NexJob.Postgres
```

---

## Quick Start

Register PostgreSQL storage in your dependency injection container **before** calling `AddNexJob()`:

```csharp
using NexJob;
using NexJob.Postgres;

var builder = WebApplication.CreateBuilder(args);

// 1. Register PostgreSQL storage
builder.Services.AddNexJobPostgres(
    builder.Configuration.GetConnectionString("NexJobConnection")!);

// 2. Register NexJob core services
builder.Services.AddNexJob(options =>
{
    options.Workers = 10;
    options.Queues = ["default", "critical"];
});
```

---

## Features

- **Atomic Job Dispatching:** Leverages PostgreSQL `FOR UPDATE SKIP LOCKED` to ensure zero lock contention and no double-execution across multiple server instances.
- **High-Throughput Batch Processing:** Dynamic batch dequeue (`FetchBatchAsync`) and vectorized batch acknowledgment (`AcknowledgeBatchAsync`) with native array operations (`WHERE id = ANY(@Ids)`).
- **Automatic Schema Migration:** Applies database migrations automatically on startup using PostgreSQL advisory locks (`pg_advisory_lock`), ensuring safe concurrent migrations in multi-node clusters.
- **Idempotency Enforcement:** Supports strict and conditional deduplication policies (`DuplicatePolicy`) indexed on `idempotency_key`.
- **Runtime Settings Store:** Persists dynamic queue throttling and runtime configuration in `nexjob_settings`.
- **Dashboard Read Replica Support:** Offload read-heavy dashboard and telemetry queries to a secondary database replica.

---

## High-Throughput Batch Processing

For heavy ingestion workloads, enable batch processing to eliminate per-job WAL transaction log flushes:

```csharp
builder.Services.AddNexJobPostgres(connectionString);

builder.Services.AddNexJob(options =>
{
    // Workers dynamically batch fetches to keep all idle slots busy (LIMIT availableWorkers)
    options.Workers = 30;
    options.PollingInterval = TimeSpan.FromMilliseconds(20);

    // Commit successful jobs in asynchronous batches via WHERE id = ANY(@Ids)
    options.EnableBatchAcknowledgment = true;
});
```

## Read Replica Configuration

If your database topology includes a PostgreSQL read replica, you can isolate dashboard queries from the write path using `UseDashboardReadReplica`:

```csharp
builder.Services.AddNexJobPostgres(primaryConnectionString);

builder.Services.AddNexJob(options =>
{
    options.Workers = 10;
})
.UseDashboardReadReplica(readReplicaConnectionString);
```

---

## Database Tables

The provider automatically manages the following tables:

| Table | Purpose |
|---|---|
| `nexjob_jobs` | Job records, payloads, state transitions, attempts, and error logs |
| `nexjob_recurring` | Recurring cron schedules and execution state |
| `nexjob_heartbeats` | Active worker node registration and health checks |
| `nexjob_settings` | Dynamic runtime configurations and queue pause flags |

---

## Connection String Example

```json
{
  "ConnectionStrings": {
    "NexJobConnection": "Host=localhost;Port=5432;Database=nexjob;Username=postgres;Password=postgres;Include Error Detail=true"
  }
}
```
