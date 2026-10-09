# NexJob Samples Catalog

Welcome to the comprehensive NexJob samples directory. This directory provides production-grade reference architectures and runnable examples covering all core execution features, storage providers, triggers, and outbox producers.

---

## Where to start

Run **`NexJob.Sample.MinimalApi`** (a web app) or **`NexJob.Sample.WorkerService`** (no web server) first. The others each show one thing, so pick by what you want to see:

| I want to see | Run |
|---|---|
| Retries, checkpoints, deadlines, dead-letter, circuit breaker | [`NexJob.Sample.Reliability`](./NexJob.Sample.Reliability) |
| The same app on PostgreSQL, SQL Server, Redis or MongoDB | [`NexJob.Sample.Providers`](./NexJob.Sample.Providers) |
| Recurring jobs from `appsettings.json` | [`NexJob.Sample.ConfiguredRecurring`](./NexJob.Sample.ConfiguredRecurring) |
| A read replica, distributed throttle and OpenTelemetry | [`NexJob.Sample.Storage`](./NexJob.Sample.Storage) |
| A broker turning messages into jobs (and an outbox back) | [`NexJob.Sample.RabbitMQ`](./NexJob.Sample.RabbitMQ), [`NexJob.Sample.Kafka`](./NexJob.Sample.Kafka), [`NexJob.Sample.CloudTriggers`](./NexJob.Sample.CloudTriggers) |
| Controllers instead of minimal endpoints | [`NexJob.Sample.WebApi`](./NexJob.Sample.WebApi) |

`Reliability` and `Providers` are demonstrations: they have one endpoint per behavior and exist to show it, so copy the pattern, not the endpoints, into your application.

---

## Architecture Matrix

| Project | Type | Storage | Broker / Triggers | Key Architectural Concepts |
|---|---|---|---|---|
| [`NexJob.Sample.MinimalApi`](./NexJob.Sample.MinimalApi) | Web (ASP.NET Core) | InMemory | None | Minimal API, deadlines, segregated `IDashboardStorage`, dead-letter handlers |
| [`NexJob.Sample.WebApi`](./NexJob.Sample.WebApi) | Web (ASP.NET Core) | InMemory / PostgreSQL | None | Clean dual-storage, controller routing, recurring jobs, tags, pause/requeue |
| [`NexJob.Sample.WorkerService`](./NexJob.Sample.WorkerService) | Background Worker | InMemory / PostgreSQL | None | Console Worker Service, standalone embedded dashboard HTTP server, graceful shutdown |
| [`NexJob.Sample.ConfiguredRecurring`](./NexJob.Sample.ConfiguredRecurring) | Web (ASP.NET Core) | InMemory | None | Declarative JSON recurring schedules (`appsettings.json`), timezones, cron expressions |
| [`NexJob.Sample.RabbitMQ`](./NexJob.Sample.RabbitMQ) | Web (ASP.NET Core) | InMemory | RabbitMQ | Outbox Producer (volatile on in-memory storage), Consumer Trigger, 5 Trigger Guarantees, auto-ack |
| [`NexJob.Sample.Kafka`](./NexJob.Sample.Kafka) | Web (ASP.NET Core) | SQL Server (InMemory if no connection string) | Apache Kafka | Event Outbox Producer, Consumer Trigger with partition commit, consumer groups |
| [`NexJob.Sample.Storage`](./NexJob.Sample.Storage) | Web (ASP.NET Core) | PostgreSQL + Redis | None | Read Replica isolation (`UseDashboardReadReplica`), Distributed Throttle, OpenTelemetry, Filter Pipeline (`IJobExecutionFilter`) |
| [`NexJob.Sample.CloudTriggers`](./NexJob.Sample.CloudTriggers) | Web (ASP.NET Core) | InMemory | AWS SQS, Azure Service Bus, GCP Pub/Sub, Salesforce | Unified cloud consumer triggers, 5 Trigger Guarantees, interactive `/simulate/*` endpoints |
| [`NexJob.Sample.Reliability`](./NexJob.Sample.Reliability) | Web (ASP.NET Core) | InMemory | None | `[Retry]`, checkpoints, deadlines, dead-letter handler, queue circuit breaker, `IJobControlService`, health checks |
| [`NexJob.Sample.Providers`](./NexJob.Sample.Providers) | Web (ASP.NET Core) | InMemory / PostgreSQL / SQL Server / Redis / MongoDB | None | The same app on any storage provider, chosen by `Sample:Provider`; copy-paste registration per provider |

---

## Local Infrastructure (Docker Compose)

A complete local development environment is provided in [`docker-compose.yml`](./docker-compose.yml):
- **PostgreSQL 16** (`localhost:5432`)
- **Redis 7** (`localhost:6379`)
- **RabbitMQ 3.13 Management** (`localhost:5672`, Management UI at `http://localhost:15672`)
- **Apache Kafka (KRaft)** (`localhost:9092`)
- **SQL Server** (`localhost:1433`), used by the Kafka and Providers samples
- **MongoDB 7** (`localhost:27017`), used by the Providers sample

### Starting Infrastructure
```bash
# Start all containers in the background
docker compose up -d

# Check service health
docker compose ps
```

### Stopping Infrastructure
```bash
docker compose down
```

---

## Ports

Every web sample ships a `Properties/launchSettings.json` with its own port, so they can run side by side:

| Sample | URL |
|---|---|
| MinimalApi | `http://localhost:5001` |
| WebApi | `http://localhost:5002` |
| ConfiguredRecurring | `http://localhost:5004` |
| WorkerService | no web app; standalone dashboard on `http://localhost:5005/dashboard` (`5006` with `--multi-cluster`) |
| Storage | `http://localhost:5007` |
| CloudTriggers | `http://localhost:5008` |
| RabbitMQ | `http://localhost:5009` |
| Kafka | `http://localhost:5010` |
| Reliability | `http://localhost:5011` |
| Providers | `http://localhost:5012` |

---

## Sample Guides

### 1. Minimal API (`NexJob.Sample.MinimalApi`)
Focuses on dead-simple background job invocation in modern .NET 8 Minimal APIs.
```bash
dotnet run --project samples/NexJob.Sample.MinimalApi/NexJob.Sample.MinimalApi.csproj
```
- Enqueue with deadline: `POST http://localhost:5001/send?email=user@example.com`
- Check job status via segregated read storage: `GET http://localhost:5001/job/{jobId}`

### 2. Full Web API (`NexJob.Sample.WebApi`)
Features a full REST API for job operations, recurring jobs, and automatic database migration.
```bash
dotnet run --project samples/NexJob.Sample.WebApi/NexJob.Sample.WebApi.csproj
```
- Dashboard UI: `http://localhost:5002/dashboard`
- Enqueue report job: `POST http://localhost:5002/jobs/report` (JSON body, see the `.http` file)
- Interactive requests: See [`NexJob.Sample.WebApi.http`](./NexJob.Sample.WebApi/NexJob.Sample.WebApi.http)

### 3. Dedicated Worker Service (`NexJob.Sample.WorkerService`)
Demonstrates how to run NexJob inside a headless .NET Worker Service (`BackgroundService`), exposing a standalone dashboard without requiring full ASP.NET Core MVC.
```bash
dotnet run --project samples/NexJob.Sample.WorkerService/NexJob.Sample.WorkerService.csproj
```
- Standalone Dashboard UI: `http://localhost:5005/dashboard` (it listens on `localhost` only)

### 4. Configured Recurring Jobs (`NexJob.Sample.ConfiguredRecurring`)
Shows declarative recurring job scheduling via `appsettings.json` without hardcoded C# cron schedules.
```bash
dotnet run --project samples/NexJob.Sample.ConfiguredRecurring/NexJob.Sample.ConfiguredRecurring.csproj
```
- Dashboard UI: `http://localhost:5004/dashboard`

### 5. RabbitMQ Outbox & Trigger (`NexJob.Sample.RabbitMQ`)
Shows how to publish messages through the reliable NexJob Outbox and consume messages from RabbitMQ queues with zero message loss.
```bash
# Ensure RabbitMQ container is running
docker compose up -d rabbitmq

dotnet run --project samples/NexJob.Sample.RabbitMQ/NexJob.Sample.RabbitMQ.csproj
```
- Publish an order through the Outbox: `POST http://localhost:5009/orders` (JSON body, see the sample README)

### 6. Kafka Outbox & Trigger (`NexJob.Sample.Kafka`)
Shows high-throughput event publishing through the Kafka Outbox and consuming partitioned topics with automatic partition offsets.
```bash
# Ensure Kafka container is running
docker compose up -d kafka

dotnet run --project samples/NexJob.Sample.Kafka/NexJob.Sample.Kafka.csproj
```
- Publish a Kafka event through the Outbox: `POST http://localhost:5010/events` (JSON body, see the sample README)
- Register customers in bulk: `POST http://localhost:5010/customers/bulk?count=10`

### 7. Storage Topology, Throttling & Observability (`NexJob.Sample.Storage`)
Demonstrates enterprise-grade storage architecture with read replica isolation, distributed Redis rate limiting, and OpenTelemetry instrumentation.
```bash
# Ensure Postgres and Redis containers are running
docker compose up -d postgres redis

dotnet run --project samples/NexJob.Sample.Storage/NexJob.Sample.Storage.csproj
```
- Trigger distributed throttle limit (concurrency = 2): `POST http://localhost:5007/payments/batch?count=5`
- Query jobs from the read connection: `GET http://localhost:5007/jobs`
- Dashboard UI: `http://localhost:5007/dashboard`

### 8. Cloud Triggers (`NexJob.Sample.CloudTriggers`)
Showcases unified trigger configurations for AWS SQS, Azure Service Bus, Google Cloud Pub/Sub, and Salesforce (gRPC Pub/Sub and CometD Streaming).
```bash
dotnet run --project samples/NexJob.Sample.CloudTriggers/NexJob.Sample.CloudTriggers.csproj
```
- View active triggers: `GET http://localhost:5008/triggers`
- Simulate AWS SQS: `POST http://localhost:5008/simulate/sqs`
- Simulate Azure Service Bus: `POST http://localhost:5008/simulate/azuresb`
- Simulate Google Pub/Sub: `POST http://localhost:5008/simulate/pubsub`
- Simulate Salesforce Pub/Sub: `POST http://localhost:5008/simulate/salesforce`
- Simulate Salesforce Streaming: `POST http://localhost:5008/simulate/salesforce-streaming`

### 9. Reliability behaviors (`NexJob.Sample.Reliability`)
One endpoint per behavior: retries, checkpoint resume, deadline expiry, dead-letter handler, queue circuit breaker, programmatic control and health checks. In-memory storage, no infrastructure.
```bash
dotnet run --project samples/NexJob.Sample.Reliability/NexJob.Sample.Reliability.csproj
```
- See [`NexJob.Sample.Reliability/README.md`](./NexJob.Sample.Reliability/README.md) for each endpoint and what to expect: `http://localhost:5011`

### 10. One app, any storage provider (`NexJob.Sample.Providers`)
The same small app on InMemory, PostgreSQL, SQL Server, Redis or MongoDB, chosen by `Sample:Provider`.
```bash
docker compose up -d postgres
dotnet run --project samples/NexJob.Sample.Providers/NexJob.Sample.Providers.csproj -- --Sample:Provider=Postgres
```
- Which provider is running: `GET http://localhost:5012/provider`
- Enqueue a job: `POST http://localhost:5012/jobs?message=hello`
