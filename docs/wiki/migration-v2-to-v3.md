# Migrating from NexJob v2 to v3

v3.0.0 introduces two breaking changes and several new features.
Standard users (using built-in providers) have minimal changes.

---

## Breaking change 1 — `AddNexJob` return type

`AddNexJob` now returns `NexJobBuilder` instead of `IServiceCollection`.

```csharp
// v2
services.AddNexJob(opt => { ... })
        .AddSingleton<MyService>();

// v3 — option A: use .Services
services.AddNexJob(opt => { ... })
        .Services
        .AddSingleton<MyService>();

// v3 — option B: chain NexJob extensions directly
services.AddNexJob(opt => { ... })
        .AddNexJobJobs(typeof(Program).Assembly)
        .UseDashboardReadReplica("replica-conn");
```

`NexJobBuilder` exposes `.Services` for cases where you need
`IServiceCollection` methods directly.

---

## Breaking change 2 — Custom storage providers

**If you did not implement a custom `IStorageProvider`, skip this section.**

`IStorageProvider` is now defined as:
```csharp
public interface IStorageProvider : IJobStorage, IRecurringStorage, IDashboardStorage { }
```

**Migration steps:**

1. Keep your class — add the three interfaces to its declaration:
```csharp
// v2
public class MyProvider : IStorageProvider { ... }

// v3
public class MyProvider : IStorageProvider { ... } // no change needed if already implements all methods
// IStorageProvider now inherits IJobStorage + IRecurringStorage + IDashboardStorage
// your class already has all the methods — just make sure it compiles
```

2. Update DI registration to register all 4 interfaces:
```csharp
services.TryAddSingleton<MyProvider>();
services.TryAddSingleton<IStorageProvider>(sp => sp.GetRequiredService<MyProvider>());
services.TryAddSingleton<IJobStorage>(sp => sp.GetRequiredService<MyProvider>());
services.TryAddSingleton<IRecurringStorage>(sp => sp.GetRequiredService<MyProvider>());
services.TryAddSingleton<IDashboardStorage>(sp => sp.GetRequiredService<MyProvider>());
```

---

## New in v3

### Read replica (PostgreSQL and SQL Server)

```csharp
services.AddNexJob(opt => opt.UsePostgres("Host=primary;..."))
        .UseDashboardReadReplica("Host=replica;...");
```

### Programmatic job control

```csharp
// Inject IJobControlService anywhere
public class AdminService(IJobControlService control) { ... }
await control.PauseQueueAsync("reports");
await control.RequeueJobAsync(jobId);
```

### Distributed throttling

```csharp
services.AddNexJob(opt =>
{
    opt.UseRedis("localhost:6379");
    opt.DistributedThrottleTtl = TimeSpan.FromHours(2);
})
.UseDistributedThrottle();
```

### Configurable throttle TTL

```csharp
options.DistributedThrottleTtl = TimeSpan.FromHours(4); // default: 1h
```

---

## Summary

| What | Required action |
|---|---|
| Built-in provider user | None — everything works as before |
| Chains `.AddSingleton` after `AddNexJob` | Add `.Services` before the chain |
| Custom storage provider | Implement 3 interfaces, register 4 DI types |
