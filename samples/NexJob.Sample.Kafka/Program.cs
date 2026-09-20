using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NexJob;
using NexJob.Dashboard;
using NexJob.Storage;

var builder = WebApplication.CreateBuilder(args);

var bootstrapServers = builder.Configuration.GetValue("Kafka:BootstrapServers", "localhost:9092")!;
var topicName = "user-events";
var groupId = "nexjob-kafka-sample-group";

// Configure NexJob with Resilient Kafka Outbox Producer and Trigger Consumer
builder.Services.AddNexJob()
    .AddKafkaProducer(opt =>
    {
        opt.BootstrapServers = bootstrapServers;
        opt.FlushTimeout = TimeSpan.FromSeconds(5);
        opt.Acks = Confluent.Kafka.Acks.All;
        opt.EnableIdempotence = true;
    })
    .AddKafkaTrigger(opt =>
    {
        opt.BootstrapServers = bootstrapServers;
        opt.Topic = topicName;
        opt.GroupId = groupId;
        opt.TargetQueue = "user-events-queue";
    })
    .AddNexJobJobs(typeof(Program).Assembly);

var app = builder.Build();

// ── Outbox Producer Endpoint ──────────────────────────────────────────────────
// Enqueues an event durably into NexJob. The background KafkaProducerJob will
// publish it to the Kafka broker with transactional guarantees and jitter retries.
app.MapPost("/events", async ([FromBody] UserEventPayload payload, IScheduler scheduler) =>
{
    var headers = new Dictionary<string, string>
    {
        ["nexjob.job_type"] = typeof(ProcessUserEventJob).AssemblyQualifiedName!,
    };

    var jobId = await scheduler.EnqueueKafkaAsync(
        topic: topicName,
        key: payload.UserId,
        value: payload,
        headers: headers,
        idempotencyKey: payload.EventId.ToString());

    return Results.Accepted($"/events/{jobId.Value}/status", new
    {
        outboxJobId = jobId.Value,
        payload.EventId,
        payload.EventType,
        status = "EnqueuedInOutbox",
        message = "Event persisted in NexJob outbox and queued for Kafka delivery.",
    });
});

// ── Status Endpoint ───────────────────────────────────────────────────────────
app.MapGet("/events/{jobId:guid}/status", async (Guid jobId, IDashboardStorage storage) =>
{
    var job = await storage.GetJobByIdAsync(new JobId(jobId));
    if (job is null)
    {
        return Results.NotFound(new { error = "Job not found", jobId });
    }

    return Results.Ok(new
    {
        job.Id,
        Status = job.Status.ToString(),
        job.Queue,
        job.Attempts,
        job.CreatedAt,
        job.CompletedAt,
        job.LastErrorMessage,
    });
});

app.UseNexJobDashboard("/dashboard");

await app.RunAsync();

// ── Models & Consumer Job ─────────────────────────────────────────────────────

public record UserEventPayload(Guid EventId, string UserId, string EventType, string Details, DateTimeOffset Timestamp);

public sealed class ProcessUserEventJob(ILogger<ProcessUserEventJob> logger) : IJob<string>
{
    public Task ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        logger.LogInformation("⚡ [Kafka Trigger] Received and processed event from topic '{Topic}': {Payload}", "user-events", input);
        return Task.CompletedTask;
    }
}
