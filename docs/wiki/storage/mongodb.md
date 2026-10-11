---
title: "MongoDB Storage Provider for NexJob Background Jobs"
sidebarTitle: "MongoDB"
description: "Use NexJob.MongoDB to store NexJob background jobs in MongoDB with atomic operations, automatic index creation, and flexible document storage."
---

NexJob's MongoDB provider stores job state as documents, using `FindOneAndUpdate` to claim jobs atomically, one `UpdateManyAsync` to acknowledge a batch, and a separate collection (`nexjob_recurring_locks`) for the recurring job locks. If MongoDB is already part of your application stack, this provider lets NexJob share your infrastructure without adding a relational database dependency.

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

## Collections and indexes

NexJob uses four collections: `nexjob_jobs`, `nexjob_recurring_jobs`, `nexjob_recurring_locks` and `nexjob_servers`. It creates the indexes it needs on first startup:

- **Jobs:** queue + status + priority + creation time (the fetch query); status + heartbeat (orphan recovery); status + scheduled time + retry time (scheduled and retried jobs); the idempotency key and the parent job id (both sparse).
- **Recurring jobs:** the next execution time.
- **Recurring locks:** a TTL index, so a lock left by a crashed node expires on its own.
- **Servers:** a TTL index on the heartbeat (one hour), so nodes that stopped reporting disappear.

Creating an index that already exists is a no-op, so restarting several nodes at once is safe.

## High-throughput batch processing

For heavy workloads, enable batch processing. Workers claim up to as many jobs as they have idle slots, each with its own atomic `FindOneAndUpdate` (the provider claims one document at a time), and acknowledge completions with one `UpdateManyAsync` and an `$in` filter over a batch of ids.

```csharp
builder.Services.AddNexJobMongoDB(
    connectionString: builder.Configuration.GetConnectionString("MongoConnection")!,
    databaseName: "nexjob");

builder.Services.AddNexJob(options =>
{
    // Workers dynamically batch-fetch to keep all idle slots busy
    options.Workers         = 30;
    options.PollingInterval = TimeSpan.FromMilliseconds(20);

    // Commit successful jobs in asynchronous batches with one UpdateManyAsync (off by default)
    options.EnableBatchAcknowledgment = true;
});
```

## DateTimeOffset serialization

From v5.6, NexJob scopes its `DateTimeOffset`-as-string serialization to its own documents only; versions before 5.6 registered a global BSON serializer. All dates in NexJob documents are stored as ISO 8601 strings at `+00:00` (UTC), ensuring correct scheduling comparisons regardless of the caller's local offset.

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

MongoDB's connection pool is controlled via the `maxPoolSize` query parameter in the connection string. The `Workers + 5` rule the SQL providers log at startup was not measured for MongoDB; start from `Workers + 10` and check your pool wait times.

```
mongodb://host:27017/nexjob?maxPoolSize=40
```

One pool serves every NexJob storage call: workers, the recurring scheduler, heartbeats and retention.

## Use cases

MongoDB is the right choice when:

- **MongoDB is already in your stack.** Reusing existing infrastructure avoids adding an extra database to operate, monitor, and back up.
- **You need distributed workers without SQL.** `FindOneAndUpdate` is atomic per document, so several worker nodes never claim the same job (covered by the multi-node reliability scenarios on MongoDB).
- **You want automatic schema management.** Collections and indexes are created on startup, with no setup scripts.
