# NexJob.Sample.CloudTriggers

Unified sample demonstrating all 5 enterprise cloud triggers available in NexJob:

1. **AWS SQS** (`NexJob.Trigger.AwsSqs`)
2. **Azure Service Bus** (`NexJob.Trigger.AzureServiceBus`)
3. **Google Cloud Pub/Sub** (`NexJob.Trigger.GooglePubSub`)
4. **Salesforce Pub/Sub API** (`NexJob.Trigger.Salesforce` - gRPC)
5. **Salesforce Streaming API** (`NexJob.Trigger.SalesforceStreaming` - CometD/Bayeux)

## How Triggers Work in NexJob

Every cloud trigger implements the **5 Guarantees of NexJob Triggers**:
- **Guarantee 1:** Never silently drops — failures to enqueue are moved to broker dead-letter / logged.
- **Guarantee 2:** Idempotency — broker message IDs/replay IDs are mapped to `JobRecord.IdempotencyKey`.
- **Guarantee 3:** Distributed Tracing — W3C `traceparent` headers are extracted and propagated into OpenTelemetry activities.
- **Guarantee 4:** Non-blocking wake-up signaling — scheduler signals worker slots immediately upon enqueue.
- **Guarantee 5:** Two-phase Ack — broker messages are only acknowledged after transactional persistence in NexJob storage.

## Running the Sample

```bash
dotnet run --project samples/NexJob.Sample.CloudTriggers/NexJob.Sample.CloudTriggers.csproj
```

The application runs on `http://localhost:5000` (or the configured ASP.NET port).

## Testing Locally (Simulation Mode)

Even without active cloud accounts, you can test all 5 trigger job handlers using the built-in HTTP simulation endpoints:

```bash
# Test AWS SQS job handler
curl -X POST http://localhost:5000/simulate/sqs

# Test Azure Service Bus job handler
curl -X POST http://localhost:5000/simulate/azuresb

# Test Google Cloud Pub/Sub job handler
curl -X POST http://localhost:5000/simulate/pubsub

# Test Salesforce Pub/Sub gRPC job handler
curl -X POST http://localhost:5000/simulate/salesforce

# Test Salesforce Streaming CometD job handler
curl -X POST http://localhost:5000/simulate/salesforce-streaming
```

Open `http://localhost:5000/dashboard` in your browser to inspect the processed jobs.

## Connecting Real Cloud Providers

To connect live brokers, fill in the credentials in `appsettings.json` or configure environment variables:

### AWS SQS
```json
"AwsSqs": {
  "QueueUrl": "https://sqs.us-east-1.amazonaws.com/123456789/my-queue"
}
```

### Azure Service Bus
```json
"AzureServiceBus": {
  "ConnectionString": "Endpoint=sb://your-namespace.servicebus.windows.net/...",
  "QueueOrTopicName": "orders"
}
```

### Google Cloud Pub/Sub
```json
"GooglePubSub": {
  "ProjectId": "your-gcp-project",
  "SubscriptionId": "orders-sub"
}
```

### Salesforce (Pub/Sub & Streaming)
```json
"Salesforce": {
  "InstanceUrl": "https://your-domain.my.salesforce.com",
  "ClientId": "<consumer-key>",
  "ClientSecret": "<consumer-secret>",
  "Username": "<user@domain.com>",
  "Password": "<password-and-token>",
  "Topic": "/data/AccountChangeEvent"
}
```
