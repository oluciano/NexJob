# NexJob Samples Catalog

Welcome to the comprehensive NexJob samples directory. This directory provides production-grade reference architectures and runnable examples covering all core execution features, storage providers, triggers, and outbox producers.

---

## Architecture Matrix

| Project | Type | Storage | Broker / Triggers | Key Architectural Concepts |
|---|---|---|---|---|
| [`NexJob.Sample.MinimalApi`](./NexJob.Sample.MinimalApi) | Web (ASP.NET Core) | InMemory | None | Minimal API, deadlines, segregated `IDashboardStorage`, dead-letter handlers |
| [`NexJob.Sample.WebApi`](./NexJob.Sample.WebApi) | Web (ASP.NET Core) | InMemory / PostgreSQL | None | Clean dual-storage, controller routing, recurring jobs, tags, pause/requeue |
| [`NexJob.Sample.WorkerService`](./NexJob.Sample.WorkerService) | Background Worker | InMemory / PostgreSQL | None | Console Worker Service, standalone embedded dashboard HTTP server, graceful shutdown |
| [`NexJob.Sample.ConfiguredRecurring`](./NexJob.Sample.ConfiguredRecurring) | Web (ASP.NET Core) | InMemory | None | Declarative JSON recurring schedules (`appsettings.json`), timezones, cron expressions |
| [`NexJob.Sample.RabbitMQ`](./NexJob.Sample.RabbitMQ) | Web (ASP.NET Core) | InMemory | RabbitMQ | Guaranteed Outbox Producer, Consumer Trigger, 5 Trigger Guarantees, auto-ack |
| [`NexJob.Sample.Kafka`](./NexJob.Sample.Kafka) | Web (ASP.NET Core) | InMemory | Apache Kafka | Event Outbox Producer, Consumer Trigger with partition commit, consumer groups |
| [`NexJob.Sample.Storage`](./NexJob.Sample.Storage) | Web (ASP.NET Core) | PostgreSQL + Redis | None | Read Replica isolation (`UseDashboardReadReplica`), Distributed Throttle, OpenTelemetry, Filter Pipeline (`IJobExecutionFilter`) |
| [`NexJob.Sample.CloudTriggers`](./NexJob.Sample.CloudTriggers) | Web (ASP.NET Core) | InMemory | AWS SQS, Azure Service Bus, GCP Pub/Sub, Salesforce | Unified cloud consumer triggers, 5 Trigger Guarantees, interactive `/simulate/*` endpoints |

---

## Local Infrastructure (Docker Compose)

A complete local development environment is provided in [`docker-compose.yml`](./docker-compose.yml):
- **PostgreSQL 16** (`localhost:5432`)
- **Redis 7** (`localhost:6379`)
- **RabbitMQ 3.13 Management** (`localhost:5672`, Management UI at `http://localhost:15672`)
- **Apache Kafka (KRaft)** (`localhost:9092`)

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

## Sample Guides

### 1. Minimal API (`NexJob.Sample.MinimalApi`)
Focuses on dead-simple background job invocation in modern .NET 8 Minimal APIs.
```bash
dotnet run --project samples/NexJob.Sample.MinimalApi/NexJob.Sample.MinimalApi.csproj
```
- Enqueue with deadline: `POST http://localhost:5000/send?email=user@example.com`
- Check job status via segregated read storage: `GET http://localhost:5000/job/{jobId}`

### 2. Full Web API (`NexJob.Sample.WebApi`)
Features a full REST API for job operations, recurring jobs, and automatic database migration.
```bash
dotnet run --project samples/NexJob.Sample.WebApi/NexJob.Sample.WebApi.csproj
```
- Dashboard UI: `http://localhost:5000/dashboard`
- Enqueue report job: `POST http://localhost:5000/api/jobs/report`
- Interactive requests: See [`NexJob.Sample.WebApi.http`](./NexJob.Sample.WebApi/NexJob.Sample.WebApi.http)

### 3. Dedicated Worker Service (`NexJob.Sample.WorkerService`)
Demonstrates how to run NexJob inside a headless .NET Worker Service (`BackgroundService`), exposing a standalone dashboard without requiring full ASP.NET Core MVC.
```bash
dotnet run --project samples/NexJob.Sample.WorkerService/NexJob.Sample.WorkerService.csproj
```
- Standalone Dashboard UI: `http://localhost:5050/jobs`

### 4. Configured Recurring Jobs (`NexJob.Sample.ConfiguredRecurring`)
Shows declarative recurring job scheduling via `appsettings.json` without hardcoded C# cron schedules.
```bash
dotnet run --project samples/NexJob.Sample.ConfiguredRecurring/NexJob.Sample.ConfiguredRecurring.csproj
```
- Dashboard UI: `http://localhost:5000/dashboard`

### 5. RabbitMQ Outbox & Trigger (`NexJob.Sample.RabbitMQ`)
Shows how to publish messages through the reliable NexJob Outbox and consume messages from RabbitMQ queues with zero message loss.
```bash
# Ensure RabbitMQ container is running
docker compose up -d rabbitmq

dotnet run --project samples/NexJob.Sample.RabbitMQ/NexJob.Sample.RabbitMQ.csproj
```
- Outbox produce: `POST http://localhost:5000/orders/outbox`
- Direct trigger enqueue: `POST http://localhost:5000/orders/direct`

### 6. Kafka Outbox & Trigger (`NexJob.Sample.Kafka`)
Shows high-throughput event publishing through the Kafka Outbox and consuming partitioned topics with automatic partition offsets.
```bash
# Ensure Kafka container is running
docker compose up -d kafka

dotnet run --project samples/NexJob.Sample.Kafka/NexJob.Sample.Kafka.csproj
```
- Produce Kafka event: `POST http://localhost:5000/events/produce?userId=USR-123&action=order_placed`

### 7. Storage Topology, Throttling & Observability (`NexJob.Sample.Storage`)
Demonstrates enterprise-grade storage architecture with read replica isolation, distributed Redis rate limiting, and OpenTelemetry instrumentation.
```bash
# Ensure Postgres and Redis containers are running
docker compose up -d postgres redis

dotnet run --project samples/NexJob.Sample.Storage/NexJob.Sample.Storage.csproj
```
- Trigger distributed throttle limit (concurrency = 2): `POST http://localhost:5000/payments/batch?count=5`
- Query jobs from read replica: `GET http://localhost:5000/jobs`
- Dashboard UI: `http://localhost:5000/dashboard`

### 8. Cloud Triggers (`NexJob.Sample.CloudTriggers`)
Showcases unified trigger configurations for AWS SQS, Azure Service Bus, Google Cloud Pub/Sub, and Salesforce (gRPC Pub/Sub and CometD Streaming).
```bash
dotnet run --project samples/NexJob.Sample.CloudTriggers/NexJob.Sample.CloudTriggers.csproj
```
- View active triggers: `GET http://localhost:5000/triggers`
- Simulate AWS SQS: `POST http://localhost:5000/simulate/sqs`
- Simulate Azure Service Bus: `POST http://localhost:5000/simulate/azuresb`
- Simulate Google Pub/Sub: `POST http://localhost:5000/simulate/pubsub`
- Simulate Salesforce Pub/Sub: `POST http://localhost:5000/simulate/salesforce`
- Simulate Salesforce Streaming: `POST http://localhost:5000/simulate/salesforce-streaming`
