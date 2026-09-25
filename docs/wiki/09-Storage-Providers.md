# Storage Providers

NexJob supports 5 storage backends. Each implements `IStorageProvider` —
a composed interface of `IJobStorage`, `IRecurringStorage`, and `IDashboardStorage`.

---

## Storage interfaces (v3)

`IStorageProvider` is composed of three focused interfaces:

| Interface | Responsibility | Inject when you need |
|---|---|---|
| `IJobStorage` | Execution, worker coordination | Custom job execution logic |
| `IRecurringStorage` | Recurring job scheduling | Custom recurring logic |
| `IDashboardStorage` | Dashboard queries and control | Custom reporting or admin |

For most applications, inject `IStorageProvider` or use `IJobControlService`
(see [IJobControlService](#ijobcontrolservice) below).
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

### Features

- Lowest latency of all providers (microsecond dispatch)
- Atomic state transitions via server-side Lua scripts
- Global distributed sliding-window throttling
- Distributed lock via `SET NX` with expiry
- Priority queues via Redis Sorted Sets (`ZSET`)

---

## MongoDB

```bash
dotnet add package NexJob.MongoDB
```

```csharp
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

### Features

- Document model matches job JSON naturally
- Atomic state transitions via `FindOneAndUpdate` with optimistic filter criteria
- Distributed recurring locks via atomic collections
- Automatic index creation on first use

---

## Provider Comparison

| Feature | InMemory | PostgreSQL | SQL Server | Redis | MongoDB |
|---|---|---|---|---|---|
| Production-ready | No | Yes | Yes | Yes | Yes |
| ACID | N/A | Yes | Yes | Partial | Partial |
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

---

## Programmatic Control with `IJobControlService`

To pause/resume queues or delete/requeue jobs programmatically outside of the dashboard UI, inject `IJobControlService`:

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
