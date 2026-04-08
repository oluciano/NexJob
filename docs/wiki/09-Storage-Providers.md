# Storage Providers

NexJob supports 5 storage backends. All implement the same `IStorageProvider` interface.

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
builder.Services.AddNexJob(options =>
{
    options.UsePostgres("Host=localhost;Database=nexjob;Username=postgres;Password=secret");
});
```

### Features

- Full ACID guarantees
- Distributed lock via advisory locks
- Dashboard queries optimized
- Automatic table creation on first use

---

## SQL Server

```bash
dotnet add package NexJob.SqlServer
```

```csharp
builder.Services.AddNexJob(options =>
{
    options.UseSqlServer("Server=localhost;Database=NexJob;Trusted_Connection=True;TrustServerCertificate=True;");
});
```

### Features

- Full ACID guarantees
- Distributed lock via `sp_getapplock`
- Automatic table creation on first use

---

## Redis

```bash
dotnet add package NexJob.Redis
```

```csharp
builder.Services.AddNexJob(options =>
{
    options.UseRedis("localhost:6379,password=secret");
});
```

### Features

- Lowest latency of all providers
- Distributed lock via `SET NX` with expiry
- Data persisted in Redis data structures
- Automatic key initialization

---

## MongoDB

```bash
dotnet add package NexJob.MongoDB
```

```csharp
builder.Services.AddNexJob(options =>
{
    options.UseMongoDB("mongodb://localhost:27017", "nexjob");
});
```

### Features

- Document model matches job JSON naturally
- Distributed lock via `findAndModify`
- Automatic collection creation
- Indexes created on first use

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

## Selecting Providers

**Development:** InMemory
**Production:** PostgreSQL or SQL Server for ACID, Redis for lowest latency, MongoDB if already in stack

---

## Next Steps

- [Dashboard](10-Dashboard.md) — Monitor jobs in storage
- [Configuration Reference](11-Configuration-Reference.md) — Provider-specific options
- [Migration](18-Migration.md) — Switch between providers
