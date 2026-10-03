---
title: "PostgreSQL Storage Provider for NexJob Background Jobs"
sidebarTitle: "PostgreSQL"
description: "Use NexJob.Postgres to store jobs in PostgreSQL with ACID guarantees, FOR UPDATE SKIP LOCKED, dashboard read replica support, and batch processing."
---

NexJob's PostgreSQL provider persists all job state in your existing PostgreSQL database using battle-tested relational primitives: `FOR UPDATE SKIP LOCKED` for contention-free worker dispatch, advisory locks for distributed coordination, and native array operations for vectorized batch acknowledgment. It is the recommended choice when your stack already uses PostgreSQL and you need full ACID guarantees across job operations.

## Install

```bash
dotnet add package NexJob.Postgres
```

## Basic setup

Register the provider **before** `AddNexJob()` so NexJob picks up the storage registration automatically.

```csharp
using NexJob.Postgres;

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

!!! note
    NexJob creates its tables and applies any pending schema migrations automatically on startup. You do not need to run SQL scripts manually or integrate with EF Core migrations.


## Configure workers and queues

Pass a configuration delegate to `AddNexJob()` to tune worker concurrency, queue names, and polling behavior:

```csharp
builder.Services.AddNexJobPostgres(
    builder.Configuration.GetConnectionString("NexJobConnection")!);

builder.Services.AddNexJob(options =>
{
    options.Workers         = 20;
    options.Queues          = ["default", "critical", "reports"];
    options.PollingInterval = TimeSpan.FromMilliseconds(50);
});
```

## Dashboard read replica

Use `UseDashboardReadReplica` to route dashboard list and metrics queries to a read replica, keeping read-heavy monitoring traffic off your primary database.

```csharp
using NexJob.Postgres;

builder.Services.AddNexJobPostgres(primaryConnectionString);

builder.Services.AddNexJob(options =>
{
    options.Workers = 10;
})
.UseDashboardReadReplica(readReplicaConnectionString);
```

`UseDashboardReadReplica` overrides only the `IDashboardStorage` registration with a separate provider instance pointed at the replica. All writes and worker coordination continue through the primary. The replica must share the same schema as the primary.

!!! tip
    This pattern works with any PostgreSQL-compatible read replica: native streaming replicas, AWS RDS read replicas, Azure Database for PostgreSQL replicas, or Citus mirror nodes.


## Storage segregation

If you need different connection strings for job operations and dashboard queries — for example, separate credentials with different permissions — replace the default `IDashboardStorage` registration directly:

```csharp
using NexJob.Postgres;
using NexJob.Storage;

builder.Services.AddNexJobPostgres(
    builder.Configuration.GetConnectionString("NexJobWorker")!);  // read-write

// Override only dashboard storage with a read-only credential
builder.Services.AddSingleton<IDashboardStorage>(_ =>
    new PostgresStorageProvider(
        builder.Configuration.GetConnectionString("NexJobReadOnly")!));

builder.Services.AddNexJob();
```

## High-throughput batch processing

For workloads that push hundreds of thousands of jobs per minute, enable batch acknowledgment together with a short polling interval. Workers dynamically fetch a batch of jobs sized to the number of idle worker slots in a single `FOR UPDATE SKIP LOCKED` query, and commit successful completions in asynchronous batches using `WHERE id = ANY(@ids)` — eliminating per-job WAL transaction flushes.

```csharp
builder.Services.AddNexJobPostgres(connectionString);

builder.Services.AddNexJob(options =>
{
    // Workers dynamically batch-fetch to keep all idle slots busy
    options.Workers         = 30;
    options.PollingInterval = TimeSpan.FromMilliseconds(20);

    // Commit successful jobs in asynchronous batches, eliminating per-job WAL flushes
    options.EnableBatchAcknowledgment = true;
});
```

## Register via NpgsqlDataSource

If your application already configures an `NpgsqlDataSource` — for example, to enable logical replication, JSON type mapping, or tracing — pass it directly so NexJob shares your existing configuration:

```csharp
using Npgsql;
using NexJob.Postgres;

var dataSource = new NpgsqlDataSourceBuilder(connectionString)
    .EnableDynamicJson()
    .Build();

builder.Services.AddNexJobPostgres(dataSource);
builder.Services.AddNexJob();
```

!!! note
    When you pass an `NpgsqlDataSource`, you own its lifetime and pool configuration. NexJob will not create a second pool alongside it.

NexJob applies its schema migrations at startup with this overload too, exactly as with a connection string, so a new database works without any preparation. Before v5.8 this overload skipped the migrations and a host on an empty database failed with `relation "nexjob_settings" does not exist`. The user behind the data source needs the privileges to create tables the first time. The dashboard read replica (`UseDashboardReadReplica`) never runs migrations.


## Connection pool sizing

NexJob holds a connection only for the duration of each storage call — a running job does not hold an open connection. The number of simultaneous connections a node needs is approximately `Workers + 5` (workers committing or heartbeating, plus the dispatcher, server heartbeat, recurring scheduler, and retention service).

Set `Maximum Pool Size` in your connection string to match:

```
Host=db;Database=nexjob;Username=u;Password=p;Maximum Pool Size=40;Application Name=nexjob-worker
```

!!! warning
    If the pool is smaller than `Workers`, workers block waiting for a connection. Calls that wait longer than the connection timeout (15 s by default) fail. NexJob logs a warning at startup when it detects this. With 4 nodes at 30 workers each, you need roughly 140 connections — more than PostgreSQL's default `max_connections` of 100. Adjust `Workers` or raise `max_connections` before scaling out.


To verify how many connections NexJob is actually using, give the connection string an `Application Name` and query `pg_stat_activity`:

```sql
SELECT application_name, count(*)
FROM pg_stat_activity
WHERE datname = 'nexjob'
GROUP BY application_name;
```
