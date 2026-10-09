---
title: "MongoDB Storage Provider for NexJob Background Jobs"
sidebarTitle: "MongoDB"
description: "Use NexJob.MongoDB to store NexJob background jobs in MongoDB with atomic operations, automatic index creation, and flexible document storage."
---

NexJob's MongoDB provider stores job state as documents, using `FindOneAndUpdate` for atomic state transitions, `UpdateManyAsync` for vectorized batch acknowledgment, and dedicated atomic collections for distributed recurring job locks. If MongoDB is already part of your application stack, this provider lets NexJob share your infrastructure without adding a relational database dependency.

## Install

```bash
dotnet add package NexJob.MongoDB
```

## Basic setup

Register the provider **before** `AddNexJob()`. Provide your MongoDB connection string and the name of the database NexJob should use.

```csharp
using NexJob.MongoDB;

// 1. Register MongoDB storage
builder.Services.AddNexJobMongoDB(
    connectionString: builder.Configuration.GetConnectionString("MongoConnection")!,
    databaseName: "nexjob");

// 2. Register NexJob core services
builder.Services.AddNexJob(options =>
{
    options.Workers = 10;
    options.QueuePrefix = "myapp"; // the "default" queue below is stored as "myapp.default"
    options.Queues  = ["default", "critical"];
});
```

The `databaseName` parameter defaults to `"nexjob"` — you can omit it if that name suits your setup.

!!! note
    NexJob creates all required collections and indexes automatically on first use. You do not need to run any setup scripts or manage MongoDB migrations manually.


## Register via an existing IMongoDatabase

If your application already configures an `IMongoDatabase` — for example, with custom serializers or a connection pool tuned for your workload — pass it directly so NexJob reuses your existing setup:

```csharp
using MongoDB.Driver;
using NexJob.MongoDB;

// Reuse your application's existing database instance
IMongoDatabase database = mongoClient.GetDatabase("myapp");

builder.Services.AddNexJobMongoDB(database);
builder.Services.AddNexJob();
```

## Index creation

NexJob creates the indexes it needs on first startup. These indexes cover:

- **Job state and queue** — for efficient worker dequeue queries
- **Scheduled time** — for future-scheduled and delayed job dispatch
- **Recurring job locks** — for distributed lease coordination across nodes

Because index creation is idempotent, restarting multiple nodes simultaneously is safe.

## High-throughput batch processing

For heavy workloads, enable batch processing so workers claim a full capacity-batch of jobs in a single `FindOneAndUpdate` pass and acknowledge completions using `UpdateManyAsync` with an `$in` filter over a batch of IDs.

```csharp
builder.Services.AddNexJobMongoDB(
    connectionString: builder.Configuration.GetConnectionString("MongoConnection")!,
    databaseName: "nexjob");

builder.Services.AddNexJob(options =>
{
    // Workers dynamically batch-fetch to keep all idle slots busy
    options.Workers         = 30;
    options.PollingInterval = TimeSpan.FromMilliseconds(20);

    // Commit successful jobs in asynchronous batches via UpdateManyAsync ($in: [ids])
    options.EnableBatchAcknowledgment = true;
});
```

## DateTimeOffset serialization

NexJob scopes its `DateTimeOffset`-as-string serialization to its own documents only — it no longer registers a global BSON serializer. All dates in NexJob documents are stored as ISO 8601 strings at `+00:00` (UTC), ensuring correct scheduling comparisons regardless of the caller's local offset.

```csharp
// If your application previously relied on NexJob's global DateTimeOffset serializer,
// pass keepLegacyGlobalDateTimeOffsetSerializer: true as a temporary measure.
// This flag will be removed in a future release.
builder.Services.AddNexJobMongoDB(
    connectionString: connectionString,
    databaseName: "nexjob",
    keepLegacyGlobalDateTimeOffsetSerializer: true);
```

!!! warning
    The `keepLegacyGlobalDateTimeOffsetSerializer` flag is a temporary migration aid. Remove it once your application explicitly configures its own `DateTimeOffset` BSON serialization. It will be removed in a future NexJob release.


## Connection pool sizing

MongoDB's connection pool is controlled via the `maxPoolSize` query parameter in the connection string. Each NexJob node needs approximately `Workers + 5` connections.

```
mongodb://host:27017/nexjob?maxPoolSize=40
```

!!! tip
    Unlike relational providers, MongoDB does not have a separate pool per operation type. One pool serves all NexJob storage calls — workers, the recurring scheduler, heartbeats, and retention all share the same pool.


## Use cases

MongoDB is the right choice when:

- **MongoDB is already in your stack.** Reusing existing infrastructure avoids adding an extra database to operate, monitor, and back up.
- **Your job payloads are document-shaped.** MongoDB's flexible schema means rich, nested job arguments serialize naturally without extra mapping.
- **You need distributed workers without SQL.** MongoDB's atomic `FindOneAndUpdate` provides safe concurrency guarantees across horizontally scaled worker nodes.
- **You want automatic schema management.** Collections and indexes are created on startup — no DBA involvement required for initial setup or when new collections are added in future NexJob versions.
