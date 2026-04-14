# NexJob v3 — Architectural Backlog

Items deferred from v2.0 — prioritized for v3 development.

## P1 — IStorageProvider Interface Segregation
Split into IJobStorage, IRecurringStorage, IDashboardStorage.
Enables Read Replica for Dashboard in Postgres/SQL Server.

## P2 — JobExecutor extraction from JobDispatcherService
Extract execution pipeline into standalone JobExecutor class.
Improves testability and isolates job failures from polling loop.

## P3 — IJobControlService
Move requeue/delete/pause operations out of DashboardMiddleware into a dedicated service.

## P4 — Distributed Throttling via Redis
[ThrottleAttribute] is currently per-instance. Add opt-in Redis backend for global throttling.

## P5 — Google Pub/Sub Testcontainers
Emulator setup deferred from v2. Evaluate Testcontainers.GooglePubSub availability.

## P6 — QChronos rename
Brand rename after v2 is stable. NexJob → QChronos on NuGet, GitHub, docs.

## P7 — Service Discovery
Auto-registration and dynamic routing of workers. No spec yet.

## Known Behaviors (not bugs)
- [ThrottleAttribute] is per-instance — effective limit = MaxConcurrent × ReplicaCount
- Clock skew affects deadline precision by ±HeartbeatTimeout
- SchemaMigrator startup contention in massive deploys is expected (advisory locks in place)
- Google Pub/Sub Testcontainers not yet implemented
