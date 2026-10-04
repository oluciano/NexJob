---
title: "SQL Server Storage Provider for NexJob Background Jobs"
sidebarTitle: "SQL Server"
description: "Use NexJob.SqlServer to persist background jobs in SQL Server with ACID guarantees, read replica support, and automatic schema setup."
---

NexJob's SQL Server provider stores all job state in your Microsoft SQL Server or Azure SQL database. It uses `WITH (UPDLOCK, READPAST, ROWLOCK)` hints for contention-free worker dispatch, `sp_getapplock` for distributed coordination, and vectorized batch acknowledgment for high-throughput workloads. Choose this provider when your application already relies on SQL Server and you need full ACID semantics with optional read replica offloading.

## Install

```bash
dotnet add package NexJob.SqlServer
```

## Basic setup

Register the provider **before** `AddNexJob()` so NexJob picks up the storage registration automatically.

```csharp
using NexJob.SqlServer;

// 1. Register SQL Server storage
builder.Services.AddNexJobSqlServer(
    builder.Configuration.GetConnectionString("NexJobConnection")!);

// 2. Register NexJob core services
builder.Services.AddNexJob(options =>
{
    options.Workers = 10;
    options.Queues  = ["default", "critical"];
});
```

!!! note
    NexJob creates its tables and applies any pending schema migrations automatically on startup. You do not need to run T-SQL scripts manually or wire up EF Core migrations.


## Dashboard read replica

Use `UseDashboardReadReplica` to route dashboard list and metrics queries to a read-scale replica, keeping monitoring traffic off your primary database. This works with Azure SQL read-scale replicas, SQL Server Always On readable secondaries, and any compatible replica endpoint.

```csharp
using NexJob.SqlServer;

builder.Services.AddNexJobSqlServer(primaryConnectionString);

builder.Services.AddNexJob(options =>
{
    options.Workers = 10;
})
.UseDashboardReadReplica(readReplicaConnectionString);
```

`UseDashboardReadReplica` overrides only the `IDashboardStorage` registration with a separate provider instance pointed at the replica. All writes and worker coordination continue through the primary connection.

!!! tip
    On Azure SQL, enable the built-in read-scale endpoint by appending `ApplicationIntent=ReadOnly` to your replica connection string and pointing it at the same server. No additional infrastructure required.


## Using an existing `SqlConnection`

`new SqlServerStorageProvider(connection, options)` takes a `SqlConnection` and opens its own connections from the connection string it keeps. With SQL Server authentication, SqlClient removes the password from `ConnectionString` the moment a connection is opened, so pass a connection that is **not open**, or add `Persist Security Info=True` to its connection string. An open connection whose password is gone is refused at construction with an `ArgumentException` that says so (before v5.8 the first new connection failed with `Login failed`). Integrated security and Microsoft Entra authentication have no password to lose and are not affected. This constructor does not apply migrations.

## Storage segregation

If you want to use separate credentials for job operations and dashboard queries — for example, a read-only database user for the dashboard — override `IDashboardStorage` directly:

```csharp
using NexJob.SqlServer;
using NexJob.Storage;

builder.Services.AddNexJobSqlServer(
    builder.Configuration.GetConnectionString("NexJobWorker")!);  // read-write

// Override only dashboard storage with a read-only credential
builder.Services.AddSingleton<IDashboardStorage>(_ =>
    new SqlServerStorageProvider(
        builder.Configuration.GetConnectionString("NexJobReadOnly")!));

builder.Services.AddNexJob();
```

## High-throughput batch processing

For workloads that stream large volumes of messages — for example, consuming from Kafka or RabbitMQ into SQL Server — enable batch processing. Workers fetch a batch of jobs sized to current idle capacity atomically in a single `TOP (@batchSize) WITH (UPDLOCK, READPAST, ROWLOCK)` statement, and complete them in asynchronous batches to eliminate per-job write round trips.

```csharp
builder.Services.AddNexJobSqlServer(connectionString);

builder.Services.AddNexJob(options =>
{
    // Workers fetch jobs in atomic batches sized to current idle capacity
    options.Workers         = 30;
    options.PollingInterval = TimeSpan.FromMilliseconds(20);

    // Commit successful jobs in asynchronous batches, eliminating per-job write round trips
    options.EnableBatchAcknowledgment = true;
});
```

## Connection pool sizing

NexJob holds a connection only for the duration of each storage call — a running job does not hold an open connection. The number of simultaneous connections a node needs is approximately `Workers + 5`.

Set `Max Pool Size` in your connection string to match:

```text
Server=db;Database=nexjob;User Id=u;Password=p;Max Pool Size=40;Application Name=nexjob-worker
```

!!! warning
    Note the different parameter name: SQL Server's `Microsoft.Data.SqlClient` uses `Max Pool Size`, while Npgsql uses `Maximum Pool Size`. An incorrect name is silently ignored, leaving the driver default of 100 connections per pool in effect. With multiple nodes, this can exceed your SQL Server's connection limit.


To verify the connections NexJob is using, query `sys.dm_exec_sessions`:

```sql
SELECT program_name, COUNT(*) AS connections
FROM sys.dm_exec_sessions
WHERE is_user_process = 1
GROUP BY program_name;
```

## Production tips


### Set Application Name

Add `Application Name=nexjob-worker` to your connection string. This labels NexJob's connections in `sys.dm_exec_sessions`, making it easy to monitor pool usage separately from other application traffic.

### Size the connection pool per node

Use the formula `Workers + 10` per node as your pool ceiling. Multiply by the number of deployed nodes and verify the total fits within SQL Server's connection limit before scaling out.

### Use read replica for dashboard

Always configure `UseDashboardReadReplica` on production deployments. Dashboard queries scan job history and can be expensive under load; routing them to a replica protects worker throughput.

### Enable batch acknowledgment for high volume

Set `EnableBatchAcknowledgment = true` when you process more than a few thousand jobs per minute. Batching acknowledgments removes the per-job write overhead that becomes the bottleneck at scale.



