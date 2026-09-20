# NexJob Sample — Apache Kafka (Resilient Outbox & Trigger)

Demonstrates NexJob's **Resilient Outbox Producer** and **Trigger Consumer** for Apache Kafka.

---

## What This Sample Shows

1. **Transactional Outbox Producer (`EnqueueKafkaAsync`)**:
   - Persists events in NexJob storage before publishing to Apache Kafka.
   - Background worker publishes to the topic with idempotence (`EnableIdempotence = true`), full acks (`Acks.All`), and jittered retries.
2. **Trigger Consumer (`AddKafkaTrigger`) & SQL Server Sink**:
   - Long-polling consumer that extracts W3C `traceparent` headers for distributed tracing.
   - Enqueues consumed messages into NexJob's internal queue with native idempotency (`TopicPartitionOffset`).
   - Commits consumer offset strictly after the job is safely committed into storage.
   - Dispatches `SaveCustomerJob` executing atomic upsert (`MERGE`) into Microsoft SQL Server with node telemetry.

---

## Quick Start

### 1. Start Kafka and SQL Server via Docker Compose

```bash
docker compose -f samples/docker-compose.yml up -d kafka sqlserver
```

### 2. Run the Sample

```bash
dotnet run --project samples/NexJob.Sample.Kafka
```

The Web API starts at `http://localhost:5000` (or `http://localhost:5247`) and the Dashboard at `/dashboard`.

---

## Testing Scenarios

### Scenario A: Ingest Customer Stream Directly from Kafka into SQL Server

Simulate an external CRM/producer writing raw customer records directly to Kafka topic `customer-registrations`:

```bash
# Produce 10 customer records directly into Kafka
curl -X POST "http://localhost:5000/customers/bulk?count=10"

# Query persisted records in SQL Server
curl "http://localhost:5000/customers"
```

### Scenario B: Publish an Event to Kafka via Outbox Producer

```bash
curl -X POST http://localhost:5000/events \
  -H "Content-Type: application/json" \
  -d '{
    "EventId": "9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d",
    "UserId": "usr-1002",
    "EventType": "UserSignup",
    "Details": "New user registered via web portal",
    "Timestamp": "2026-09-19T12:30:00Z"
  }'
```

Watch console logs:
1. Event is enqueued into NexJob Outbox queue (`kafka-producer`).
2. Producer publishes to Kafka topic `user-events`.
3. Kafka Trigger consumes the message.
4. `ProcessUserEventJob` executes and logs the event!

