# NexJob Sample — Web API

Comprehensive ASP.NET Core Web API demonstrating NexJob background job execution, recurring schedules, job continuations, priority queues, and dashboard integration with support for both **In-Memory** and **PostgreSQL** storage providers.

---

## Features Demonstrated

- **Storage Flexibility**: Runs In-Memory by default, or with PostgreSQL simply by providing `ConnectionStrings:NexJobPostgres`.
- **Job Lifecycles**: Fire-and-forget, delayed scheduling, recurring cron schedules, job continuations (`ContinueWithAsync`), and priority routing.
- **Embedded Dashboard**: Visual dashboard accessible at `http://localhost:5000/dashboard`.
- **Segregated Storage Contracts**: Uses `IDashboardStorage` and `IRecurringStorage` instead of monolithic storage interfaces.
- **Idempotent Enqueueing**: Native idempotency keys preventing duplicate executions under concurrent load.

---

## Quick Start

### 1. Run with In-Memory Storage (Zero Setup)

```bash
dotnet run --project samples/NexJob.Sample.WebApi
```

### 2. Run with PostgreSQL Storage

Start the local database container via docker compose:

```bash
docker compose -f samples/docker-compose.yml up -d postgres
```

Ensure `ConnectionStrings:NexJobPostgres` is configured in `appsettings.json`:

```json
"ConnectionStrings": {
  "NexJobPostgres": "Host=localhost;Port=5432;Database=nexjob_sample;Username=postgres;Password=postgres"
}
```

Then run the application:

```bash
dotnet run --project samples/NexJob.Sample.WebApi
```

The Web API starts on `http://localhost:5000` and the Dashboard is accessible at `http://localhost:5000/dashboard`.

---

## API Testing Examples (cURL)

### Enqueue an Email Job (Fire-and-Forget)

```bash
curl -X POST http://localhost:5000/jobs/email \
  -H "Content-Type: application/json" \
  -d '{"To": "dev@example.com", "Subject": "Hello NexJob", "Body": "Testing background processing"}'
```

### Check Job Status

```bash
curl http://localhost:5000/jobs/{job-id}/status
```

### Schedule a Delayed Job (Runs after 10s)

```bash
curl -X POST http://localhost:5000/jobs/report/schedule \
  -H "Content-Type: application/json" \
  -d '{"Report": {"ReportName": "Monthly-Sales", "From": "2026-01-01", "To": "2026-01-31"}, "DelaySeconds": 10}'
```

### Register a Recurring Job

```bash
curl -X POST http://localhost:5000/jobs/cleanup/recurring
```

### Job Continuation (Chain Email after Report)

```bash
curl -X POST http://localhost:5000/jobs/chain \
  -H "Content-Type: application/json" \
  -d '{"To": "admin@example.com", "Subject": "Report Finished", "Body": "Your sales report is ready."}'
```
