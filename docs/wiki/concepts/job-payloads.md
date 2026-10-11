---
title: "NexJob Job Payloads: Serialization and Schema Versioning"
sidebarTitle: "Job payloads"
description: "How NexJob stores a job's input as JSON, and how to change an input type safely with [SchemaVersion] and IJobMigration so jobs already in the queue keep working."
---

A job's input is serialized to JSON when you enqueue it and read back when a worker runs it, which can be hours or days later and on a newer version of your code. This page covers what is stored, and how to change an input type without breaking the jobs that are already waiting.

## What is stored

Three fields of the [`JobRecord`](../reference/job-record.md) describe the input:

| Field | Holds |
|---|---|
| `InputType` | The assembly-qualified name of the input type, used to deserialize it. |
| `InputJson` | The input, serialized with `System.Text.Json`. |
| `SchemaVersion` | The version declared on the job class with `[SchemaVersion(n)]`, or `1` when the attribute is absent. |

NexJob records the version when the job is enqueued, so the stored payload is always labelled with the version it was written in.

## Serialization rules

- The input type must be serializable with `System.Text.Json`.
- Prefer `record` types: they are immutable, concise and serialize predictably.
- Keep the input minimal: identifiers and the values the job needs, not entire entity graphs.
- Avoid types that cannot survive a round trip, such as delegates, streams or non-serializable structs.

## Changing an input type

When you change the shape of an input, jobs already in the queue were written with the old shape. Declare the new version and give NexJob a migration, and it upgrades those payloads when they run.

Each version gets its own input type, because NexJob finds the migration to run from the type it is converting **to**.

```csharp
// Old input (version 1)
public sealed record SendEmailInputV1(string To, string Subject);

// New input (version 2)
public sealed record SendEmailInputV2(string To, string Subject, string ReplyTo);

// Migration from version 1 to version 2
public sealed class SendEmailV1ToV2 : IJobMigration<SendEmailInputV1, SendEmailInputV2>
{
    public SendEmailInputV2 Migrate(SendEmailInputV1 old) =>
        new(old.To, old.Subject, ReplyTo: "noreply@example.com");
}
```

Register the migration, and declare the version on the job class:

```csharp
builder.Services.AddJobMigration<SendEmailInputV1, SendEmailInputV2, SendEmailV1ToV2>();
```

```csharp
[SchemaVersion(2)]
public sealed class SendEmailJob : IJob<SendEmailInputV2>
{
    public async Task ExecuteAsync(SendEmailInputV2 input, CancellationToken ct)
    {
        // input is always version 2: older payloads were migrated before this call
    }
}
```

When a worker fetches a job whose stored version is lower than the class version, it deserializes the payload as the old type, runs the migration and passes the new type to `ExecuteAsync`. The stored job is not rewritten: the migration runs each time the job executes.

## Several versions

Each step is its own migration. A job stored at version 1 on a class that is now at version 3 runs the migrations from 1 to 2, then from 2 to 3, in that order:

```csharp
services.AddJobMigration<SendEmailInputV1, SendEmailInputV2, SendEmailV1ToV2>();
services.AddJobMigration<SendEmailInputV2, SendEmailInputV3, SendEmailV2ToV3>();
```

If a step is missing, or a migration is not registered in the container, the run throws an `InvalidOperationException` that names the type and the versions involved. Register every step before you raise the version.

## During a rolling deployment

A migration only goes forward. When the stored version is equal to or higher than the version of the class a node is running, the payload is passed through unchanged and deserialized as that node's own input type. During a rolling deployment, an older node can therefore receive a payload written by a newer node. System.Text.Json ignores properties the old type does not have, so an additive change is safe; a change that removes or renames a property is not, so roll the new version out to every node before enqueuing with it.

## Jobs enqueued by earlier releases

Releases before this behavior did not record the version: every job was stored as version 1, whatever the attribute said. Those jobs keep that value, so a job written with a version 2 payload that is still queued will be migrated as if it were version 1. If you raised `[SchemaVersion]` while jobs were queued, let them drain before relying on the migration.

## Next

- [Job types](job-types.md): `IJob` and `IJob<TInput>`.
- [`JobRecord` reference](../reference/job-record.md): every field stored for a job.
- [Migration guide](../reference/migration.md): breaking changes between NexJob versions.
