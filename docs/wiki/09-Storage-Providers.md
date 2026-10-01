# Storage Providers

NexJob supports 5 storage backends. Each implements `IStorageProvider` —
a composed interface of `IJobStorage`, `IRecurringStorage`, and `IDashboardStorage`.

---

## Storage interfaces

`IStorageProvider` is composed of three focused interfaces:

| Interface | Responsibility | Inject when you need |
|---|---|---|
| `IJobStorage` | Execution, worker coordination | Custom job execution logic |
| `IRecurringStorage` | Recurring job scheduling | Custom recurring logic |
| `IDashboardStorage` | Dashboard queries and control | Custom reporting or admin |

For most applications, inject `IStorageProvider` or use `IJobControlService`
(see [Programmatic Control](#programmatic-control-with-ijobcontrolservice) below).
Built-in providers implement all three — no registration changes needed.

---

## InMemory (Default)

Built-in. No dependencies. Ideal for development and testing.

```csharp
builder.Services.AddNexJob(); // InMemory by default

// Or explicit
builder.Services.AddNexJob(options => options.UseInMemory());
```

**Not recommended for production.** Data is lost on restart.

---

## PostgreSQL

```bash
dotnet add package NexJob.Postgres
```

```csharp
using NexJob.Postgres;   // AddNexJobPostgres, UseDashboardReadReplica

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

### Dashboard Read Replica Support

Offload read-heavy dashboard and telemetry queries to a secondary database replica using `UseDashboardReadReplica`:

```csharp
builder.Services.AddNexJobPostgres(primaryConnectionString);

builder.Services.AddNexJob(options =>
{
    options.Workers = 10;
})
.UseDashboardReadReplica(readReplicaConnectionString);
```

- Full ACID guarantees
- Distributed lock via advisory locks
- Concurrency-safe job fetching via `FOR UPDATE SKIP LOCKED`
- Dynamic batch dequeue (`FetchBatchAsync`) based on idle worker capacity
- Vectorized batch acknowledgment (`AcknowledgeBatchAsync`) using native PostgreSQL array operations (`WHERE id = ANY(@Ids)`)
- Dashboard Read Replica offloading
- Automatic table creation and schema migrations on startup

### High-Throughput Batch Processing Example

For extreme workloads on PostgreSQL, enable batch processing:

```csharp
builder.Services.AddNexJobPostgres(connectionString);

builder.Services.AddNexJob(options =>
{
    // Workers dynamically batch fetches to keep all idle slots busy (LIMIT availableWorkers)
    options.Workers = 30;
    options.PollingInterval = TimeSpan.FromMilliseconds(20);

    // Commit successful jobs in asynchronous batches, eliminating per-job WAL transaction log flushes
    options.EnableBatchAcknowledgment = true;
});
```

---

## SQL Server

```bash
dotnet add package NexJob.SqlServer
```

```csharp
using NexJob.SqlServer;   // AddNexJobSqlServer, UseDashboardReadReplica

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

### Dashboard Read Replica Support

Route dashboard metrics and monitoring queries to an Azure SQL / SQL Server read-scale replica:

```csharp
builder.Services.AddNexJobSqlServer(primaryConnectionString);

builder.Services.AddNexJob(options =>
{
    options.Workers = 10;
})
.UseDashboardReadReplica(readReplicaConnectionString);
```

- Full ACID guarantees
- Distributed lock via `sp_getapplock`
- Concurrency-safe job fetching via `WITH (UPDLOCK, READPAST, ROWLOCK)`
- Dynamic batch dequeue (`FetchBatchAsync`) based on idle worker capacity
- Vectorized batch acknowledgment (`AcknowledgeBatchAsync`) for ultra-high throughput
- Dashboard Read Replica offloading
- Automatic table creation and schema migrations on startup

### High-Throughput Batch Processing Example

For extreme workloads (e.g. streaming hundreds of thousands of events from Kafka/RabbitMQ into SQL Server), enable batch processing:

```csharp
builder.Services.AddNexJobSqlServer(connectionString);

builder.Services.AddNexJob(options =>
{
    // Workers will fetch jobs in atomic batches up to current idle capacity (TOP availableWorkers)
    options.Workers = 30;
    options.PollingInterval = TimeSpan.FromMilliseconds(20);

    // Commit successful jobs in asynchronous batches, eliminating per-job write roundtrips
    options.EnableBatchAcknowledgment = true;
});
```

---

## Redis

```bash
dotnet add package NexJob.Redis
```

```csharp
using NexJob.Redis;   // AddNexJobRedis, AddNexJobDistributedThrottle

// 1. Register Redis storage
builder.Services.AddNexJobRedis("localhost:6379,abortConnect=false");

// 2. Register NexJob core services
builder.Services.AddNexJob(options =>
{
    options.Workers = 10;
    options.Queues = ["default", "critical"];
});
```

### Distributed Throttling

Enable global, cluster-wide rate limiting across multiple worker nodes or containers:

```csharp
builder.Services.AddNexJobRedis("localhost:6379")
    .AddNexJobDistributedThrottle();
```

### High-Throughput Batch Processing Example

For extreme ingestion workloads (e.g. consuming tens of thousands of messages from Kafka/RabbitMQ into Redis), enable batch processing to leverage server-side Lua scripts and vectorized acknowledgments:

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

### Features

- Lowest latency of all providers (microsecond dispatch)
- High-throughput batch fetching and acknowledgment via optimized server-side Lua scripts (`FetchBatchScript`, `AcknowledgeBatchScript`)
- Atomic state transitions via server-side Lua scripts
- Optional cluster-wide `[Throttle]` limits (`AddNexJobDistributedThrottle()`): every running job holds an expiring entry that its node keeps refreshing, so slots left by a crashed node are reclaimed automatically
- Distributed lock via `SET NX` with expiry
- Priority queues via Redis Sorted Sets (`ZSET`)
- A sorted-set job index keeps dashboard lists, metrics and retention from scanning the whole keyspace

---

## MongoDB

```bash
dotnet add package NexJob.MongoDB
```

```csharp
using NexJob.MongoDB;   // AddNexJobMongoDB

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

### High-Throughput Batch Processing Example

For extreme workloads on MongoDB, batching groups job reservations and vectorized acknowledgments using `UpdateManyAsync`:

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

### Features

- Document model matches job JSON naturally
- High-throughput batch claim and vectorized acknowledgment (`UpdateManyAsync` by ID set)
- Atomic state transitions via `FindOneAndUpdate` with optimistic filter criteria
- Distributed recurring locks via atomic collections
- NexJob no longer registers a global `DateTimeOffset` serializer: the string representation applies to NexJob's own documents only, so your application's `DateTimeOffset` values are serialized the way *you* configured them. If your application unknowingly relied on the old global registration, pass `keepLegacyGlobalDateTimeOffsetSerializer: true` to `AddNexJobMongoDB` (temporary; it will be removed in a later release)
- Every date is stored as an ISO 8601 string at `+00:00` (UTC), so scheduling comparisons are correct whatever offset the caller used. Documents written by older versions with a non-UTC offset keep that offset until they are rewritten; only future-scheduled jobs created with a local offset are affected
- Automatic index creation on first use

---

## Database connections and pool sizing

NexJob usually shares its database with other applications, so how many connections it keeps open matters. A node reuses pooled connections: each storage call takes one and returns it right away, and a job that is running does not hold a connection.

**What a node uses.** Measured on PostgreSQL, one node, 3000 jobs of 200 ms, counting connections on the server:

| Workers | Highest number of connections seen |
|---|---|
| 5 | 10 |
| 30 | 35 |
| 60 | 60 |

So a node needs about **`Workers` + 5** connections (one per worker that is committing or heartbeating, plus the dispatcher, the server heartbeat, the recurring scheduler and retention). This was measured on PostgreSQL only; SQL Server follows the same pattern in the code but was not measured.

**Sizing rule.** Set the pool of each node to about `Workers` + 10, then multiply by the number of nodes and compare with the database limit that is left after the other applications. Example: 4 nodes with 30 workers each need about 140 connections, more than PostgreSQL's default `max_connections` of 100. Either lower `Workers` or raise the database limit before you scale out.

**Where to set it.** NexJob does not set a pool size, so the driver default applies (100 per pool, per node). Set it in the connection string:

| Provider | Setting | Example |
|---|---|---|
| PostgreSQL (Npgsql) | `Maximum Pool Size` | `Host=db;Database=nexjob;Username=u;Password=p;Maximum Pool Size=40;Application Name=nexjob-worker` |
| SQL Server (SqlClient) | `Max Pool Size` | `Server=db;Database=nexjob;User Id=u;Password=p;Max Pool Size=40;Application Name=nexjob-worker` |
| MongoDB | `maxPoolSize` | `mongodb://host/?maxPoolSize=40` |
| Redis | one shared multiplexer per process | no pool to size |

The option has a different name in each driver (`Maximum Pool Size` for Npgsql, `Max Pool Size` for SqlClient), and a wrong name is an error or is ignored.

**Too small or too large.** If the pool is smaller than `Workers`, workers wait for a connection and a call that waits longer than the connection timeout (15 seconds by default) fails. NexJob logs a warning at startup when it sees this. If the pool is much larger than you need, nothing breaks in NexJob, but a busy period can take connections the other applications on the same database need.

**At startup** NexJob logs one line with the pool size it found (`NexJob database pool (PostgreSQL): Maximum Pool Size = 100, Workers = 10 ...`), or a warning when the pool is smaller than `Workers`. A host with `Workers = 0` (dashboard only) logs nothing.

**One pool per node.** With `AddNexJobPostgres(connectionString)` the storage and the runtime settings store share one pool (before v5.7.0 they opened two, so the limit applied twice). With `AddNexJobPostgres(NpgsqlDataSource)` you own the data source, so configure the pool there. `UseDashboardReadReplica` opens its own pool on purpose, because it talks to another server.

**See it on the database.** Give the connection an `Application Name` and count the sessions:

```sql
-- PostgreSQL
SELECT application_name, count(*) FROM pg_stat_activity
WHERE datname = 'nexjob' GROUP BY application_name;

-- SQL Server
SELECT program_name, COUNT(*) FROM sys.dm_exec_sessions
WHERE is_user_process = 1 GROUP BY program_name;
```

For SQL Server, `AddNexJobSqlServer(connectionString)` shares one pool between storage and settings. If you build `SqlServerStorageProvider` yourself from a `SqlConnection`, pass one that has not been opened: SqlClient may return the connection string without the password after the first open.

---

## Provider Comparison

| Feature | InMemory | PostgreSQL | SQL Server | Redis | MongoDB |
|---|---|---|---|---|---|
| Production-ready | No | Yes | Yes | Yes | Yes |
| ACID | N/A | Yes | Yes | Partial | Partial |
| High-Throughput Batching | Native | Native (`FOR UPDATE SKIP LOCKED`) | Native (`UPDLOCK, READPAST`) | Native (Lua Scripts) | Native (`UpdateMany`) |
| Distributed lock | N/A | Yes | Yes | Yes | Yes |
| Auto-create schema | N/A | Yes | Yes | Yes | Yes |
| Dashboard support | Yes | Yes | Yes | Yes | Yes |
| Runtime settings store | In-memory | Persistent | Persistent | Persistent | Persistent |

---

## Runtime Settings Store

All persistent providers implement `IRuntimeSettingsStore`. This stores dashboard-modifiable settings (paused queues, worker count, polling interval) in storage.

- Settings survive restarts
- Multiple worker nodes see the same settings
- InMemory falls back to an in-memory store (settings lost on restart)

---

## Programmatic Control with `IJobControlService`

To pause/resume queues, delete/requeue jobs or reset a queue's circuit breaker programmatically outside of the dashboard UI, inject `IJobControlService`. It exposes `RequeueJobAsync`, `DeleteJobAsync`, `PauseQueueAsync`, `ResumeQueueAsync` and `ResetQueueCircuitAsync`:

```csharp
public sealed class MaintenanceService(IJobControlService control)
{
    public async Task PerformMaintenanceAsync(JobId jobId, CancellationToken ct)
    {
        // Pause a queue to prevent new workers from dequeuing
        await control.PauseQueueAsync("reports", ct);

        // Requeue or delete specific jobs
        await control.DeleteJobAsync(jobId, ct);

        // Resume processing
        await control.ResumeQueueAsync("reports", ct);
    }
}
```

`IJobControlService` is registered automatically as a singleton by `AddNexJob()`.

---

## Selecting Providers

- **Development & Testing:** `InMemory` (zero infrastructure required)
- **Production (Relational):** `PostgreSQL` or `SQL Server` for full ACID transactions and Read Replica offloading
- **Production (High-Throughput):** `Redis` for sub-millisecond dispatching latencies and distributed throttling
- **Production (Document):** `MongoDB` if already in your application stack

---

## Next Steps

- [Dashboard](10-Dashboard.md) — Monitor jobs in storage
- [Configuration Reference](11-Configuration-Reference.md) — Provider-specific options
- [Migration](18-Migration.md) — Switch between providers and handle schema migrations
