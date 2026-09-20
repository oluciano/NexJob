# NexJob Sample — RabbitMQ (Resilient Outbox & Trigger)

Demonstrates NexJob's **Resilient Outbox Producer** and **Trigger Consumer** for RabbitMQ.

---

## What This Sample Shows

1. **Transactional Outbox Producer (`EnqueueRabbitMqAsync`)**:
   - Instead of publishing directly to RabbitMQ during an HTTP request (which fails if the broker is unreachable), the message is enqueued durably in NexJob storage.
   - A dedicated worker publishes the message with broker **Publisher Confirms** and exponential backoff retry.
2. **Trigger Consumer (`AddRabbitMqTrigger`)**:
   - Connects to RabbitMQ with W3C `traceparent` context extraction.
   - Automatically enqueues consumed messages into NexJob execution pipeline with native broker idempotency.
   - Acknowledges (`BasicAck`) strictly after successful enqueue.

---

## Quick Start

### 1. Start RabbitMQ via Docker Compose

```bash
docker compose -f samples/docker-compose.yml up -d rabbitmq
```

RabbitMQ Management UI will be available at `http://localhost:15672` (Login: `guest` / `guest`).

### 2. Run the Sample

```bash
dotnet run --project samples/NexJob.Sample.RabbitMQ
```

The Web API starts at `http://localhost:5000` and Dashboard at `http://localhost:5000/dashboard`.

---

## Testing via cURL

### Publish an Order via Outbox

```bash
curl -X POST http://localhost:5000/orders \
  -H "Content-Type: application/json" \
  -d '{
    "OrderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "CustomerEmail": "buyer@example.com",
    "Amount": 249.90,
    "PlacedAt": "2026-09-19T12:00:00Z"
  }'
```

Watch console logs:
1. NexJob enqueues the outbox job.
2. The Outbox producer publishes the message to RabbitMQ with publisher confirms.
3. The RabbitMQ Trigger consumes the message from `orders.incoming`.
4. `ProcessOrderJob` executes and logs the received payload!
