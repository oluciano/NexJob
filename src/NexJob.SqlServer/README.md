# NexJob.SqlServer

Microsoft SQL Server storage provider for **NexJob** — the background job scheduler for .NET 8+.

Backed by **Microsoft.Data.SqlClient** and **Dapper**, this provider uses `WITH (UPDLOCK, READPAST, ROWLOCK)` hints for atomic, lock-contention-free job fetching across multiple distributed worker nodes.

---

## Installation

```bash
dotnet add package NexJob.SqlServer
```

---

## Quick Start

Register SQL Server storage in your dependency injection container **before** calling `AddNexJob()`:

```csharp
using NexJob;
using NexJob.SqlServer;

var builder = WebApplication.CreateBuilder(args);

// 1. Register SQL Server storage
builder.Services.AddNexJobSqlServer(
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

- **Atomic Job Dispatching:** Employs `WITH (UPDLOCK, READPAST, ROWLOCK)` locking hints to allow multiple workers to concurrently dequeue jobs without blocking each other or causing deadlocks.
- **Automatic Schema Migration:** Applies database schema migrations automatically on startup using `sp_getapplock`, guaranteeing safe concurrent execution in clustered environments.
- **Idempotency Enforcement:** Enforces strict and conditional deduplication policies (`DuplicatePolicy`) indexed on `idempotency_key`.
- **Runtime Settings Store:** Persists queue concurrency rules and runtime settings in `nexjob_settings`.
- **Dashboard Read Replica Support:** Route dashboard metrics and monitoring queries to an Azure SQL / SQL Server read-scale replica.

---

## Read Replica Configuration

To offload dashboard queries to a read replica (such as Azure SQL Database Read-Scale Out or Always On Availability Groups):

```csharp
builder.Services.AddNexJobSqlServer(primaryConnectionString);

builder.Services.AddNexJob(options =>
{
    options.Workers = 10;
})
.UseDashboardReadReplica(readReplicaConnectionString);
```

---

## Database Tables

The provider automatically manages the following tables in the configured database:

| Table | Purpose |
|---|---|
| `nexjob_jobs` | Job records, serialized arguments, execution state, retries, and errors |
| `nexjob_recurring` | Recurring cron schedules and execution state |
| `nexjob_heartbeats` | Active worker node registration and health status |
| `nexjob_settings` | Dynamic runtime settings and queue controls |

---

## Connection String Example

```json
{
  "ConnectionStrings": {
    "NexJobConnection": "Server=tcp:sqlserver.database.windows.net,1433;Initial Catalog=nexjob;User ID=admin;Password=secret;Encrypt=True;TrustServerCertificate=False;"
  }
}
```
