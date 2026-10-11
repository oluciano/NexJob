---
title: "PostgreSQL Storage Provider for NexJob Background Jobs"
sidebarTitle: "PostgreSQL"
description: "Use NexJob.Postgres to store jobs in PostgreSQL with ACID guarantees, FOR UPDATE SKIP LOCKED, dashboard read replica support, and batch processing."
---

NexJob's PostgreSQL provider persists all job state in your existing PostgreSQL database using plain relational primitives: `FOR UPDATE SKIP LOCKED` so workers on many nodes never claim the same job, an advisory lock so only one node migrates the schema at a time, and `WHERE id = ANY(@ids)` to acknowledge a batch of jobs in one statement. It is the recommended choice when your stack already uses PostgreSQL and you need full ACID guarantees across job operations.

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
    options.QueuePrefix = "myapp"; // the "default" queue below is stored as "myapp.default"
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

`UseDashboardReadReplica` overrides only the `IDashboardStorage` registration with a separate provider instance pointed at the replica. Enqueueing, fetching and the worker coordination continue through the primary, and the replica must share the same schema. It never runs migrations.

!!! note "Requeue and delete use the primary"
    `IJobControlService.RequeueJobAsync` and `DeleteJobAsync` write to the primary, not to the replica, and the orphan watcher reads a failed job from the primary before it calls the dead-letter handler. The replica serves only the dashboard's read queries, so a replica that lags never hides a job from those actions.

!!! tip
    The replica only has to be a PostgreSQL database with the same schema that the connection string can read. The tests behind this page used a read-only role on the primary database (see the next section); a streaming or managed replica was not tried.


## Storage segregation

If you want the dashboard to connect with a **different credential**, for example one that can only read, point `UseDashboardReadReplica` at the same database with that credential:

```csharp
builder.Services.AddNexJobPostgres(
    builder.Configuration.GetConnectionString("NexJobWorker")!);   // read-write

builder.Services.AddNexJob()
    .UseDashboardReadReplica(builder.Configuration.GetConnectionString("NexJobReadOnly")!);
```

The read-only role needs `CONNECT` on the database, `USAGE` on the schema and `SELECT` on the NexJob tables (checked: `GetMetricsAsync` works with exactly these). The same limit as above applies to requeue and delete.

!!! warning "Do not build the provider from a connection string for this"
    `new PostgresStorageProvider(connectionString)` applies the schema migrations in its constructor, and the migrator runs `CREATE TABLE IF NOT EXISTS`, which needs the `CREATE` privilege on the schema even when the tables already exist. With a read-only credential it fails at startup with `permission denied for schema public`. The `NpgsqlDataSource` constructor and `UseDashboardReadReplica` do not migrate.

## High-throughput batch processing

For workloads that push hundreds of thousands of jobs per minute, enable batch acknowledgment together with a short polling interval. Workers dynamically fetch a batch of jobs sized to the number of idle worker slots in a single `FOR UPDATE SKIP LOCKED` query, and commit successful completions in asynchronous batches using `WHERE id = ANY(@ids)`, one statement for many jobs instead of one commit per job.

```csharp
builder.Services.AddNexJobPostgres(connectionString);

builder.Services.AddNexJob(options =>
{
    // Workers dynamically batch-fetch to keep all idle slots busy
    options.Workers         = 30;
    options.PollingInterval = TimeSpan.FromMilliseconds(20);

    // Commit successful jobs in asynchronous batches (off by default)
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

```text
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
