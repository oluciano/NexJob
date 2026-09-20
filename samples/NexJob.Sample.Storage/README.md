# NexJob.Sample.Storage

Production-ready sample demonstrating enterprise storage topology, distributed throttling, OpenTelemetry observability, and execution filters.

## Architectural Patterns Demonstrated

1. **Read Replica Segregation (`UseDashboardReadReplica`)**:
   - Primary PostgreSQL database handles transactional job state transitions (`IJobStorage`).
   - Read replica offloads heavy dashboard analytics and queue inspection queries (`IDashboardStorage`).
2. **Global Distributed Throttling (`UseDistributedThrottle`)**:
   - `[Throttle("payment-gateway", 2)]` limits concurrency across **all** worker instances using Redis atomic counters.
3. **OpenTelemetry Instrumentation**:
   - Distributed tracing and metrics exports out-of-the-box (`AddNexJobInstrumentation`).
4. **Execution Middleware Pipeline (`IJobExecutionFilter`)**:
   - `TimingAndAuditFilter` intercepts job lifecycle, records execution latency, and logs audit context.

## Prerequisites

Start the local infrastructure using Docker Compose from the `samples` directory:

```bash
docker compose up -d postgres redis
```

## Running the Sample

```bash
dotnet run --project samples/NexJob.Sample.Storage/NexJob.Sample.Storage.csproj
```

The application starts on `http://localhost:5000` (or the configured ASP.NET port).

## Testing Scenarios

### 1. Trigger Distributed Throttling
Enqueue 5 payments at once. Observe in console logs that only **2** payments execute concurrently, while the remaining jobs wait:

```bash
curl -X POST "http://localhost:5000/payments/batch?count=5"
```

### 2. View Read Replica Storage Queries
Inspect jobs through the segregated read replica storage provider:

```bash
curl http://localhost:5000/jobs
```

### 3. Open NexJob Dashboard
Open `http://localhost:5000/dashboard` in your browser. All metrics and queue queries hit the replica database!
