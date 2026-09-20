# NexJob Sample — Worker Service

Dedicated background worker service demonstrating headless NexJob execution with the **Embedded Standalone Dashboard**.

---

## Features Demonstrated

- **Standalone Host**: Runs as a standard .NET `IHostedService` background worker without requiring an ASP.NET Core Web API project.
- **Embedded Standalone Dashboard**: Embeds a lightweight HTTP server (`NexJob.Dashboard.Standalone`) exposing the dashboard at `http://localhost:5005/dashboard`.
- **Automatic Assembly Job Discovery**: Registers all jobs in the assembly cleanly with `.AddNexJobJobs(typeof(Program).Assembly)`.
- **PostgreSQL & In-Memory Switching**: Seamlessly runs in-memory or connects to a PostgreSQL instance via `ConnectionStrings:NexJobPostgres`.

---

## Quick Start

### 1. Run with In-Memory Storage

```bash
dotnet run --project samples/NexJob.Sample.WorkerService
```

### 2. Run with PostgreSQL Storage

Start the local PostgreSQL container:

```bash
docker compose -f samples/docker-compose.yml up -d postgres
```

Configure `ConnectionStrings:NexJobPostgres` in `appsettings.json`:

```json
"ConnectionStrings": {
  "NexJobPostgres": "Host=localhost;Port=5432;Database=nexjob_sample;Username=postgres;Password=postgres"
}
```

Run the worker:

```bash
dotnet run --project samples/NexJob.Sample.WorkerService
```

Open the dashboard in your browser at:
👉 **`http://localhost:5005/dashboard`**
