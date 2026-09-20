using Microsoft.AspNetCore.Mvc;
using NexJob;
using NexJob.Dashboard;
using NexJob.Storage;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

var rabbitHost = builder.Configuration.GetValue("RabbitMQ:HostName", "localhost")!;
var rabbitPort = builder.Configuration.GetValue("RabbitMQ:Port", 5672);
var queueName = "orders.incoming";

// Setup RabbitMQ connection factory
builder.Services.AddSingleton<IConnectionFactory>(_ => new ConnectionFactory
{
    HostName = rabbitHost,
    Port = rabbitPort,
    UserName = "guest",
    Password = "guest",
    DispatchConsumersAsync = true,
});

// Configure NexJob with Outbox Producer and Trigger Consumer
builder.Services.AddNexJob()
    .AddRabbitMqProducer(opt =>
    {
        opt.HostName = rabbitHost;
        opt.Port = rabbitPort;
        opt.DefaultExchange = string.Empty;
        opt.DefaultRoutingKey = queueName;
        opt.ConfirmTimeout = TimeSpan.FromSeconds(5);
    })
    .AddRabbitMqTrigger(opt =>
    {
        opt.HostName = rabbitHost;
        opt.Port = rabbitPort;
        opt.QueueName = queueName;
        opt.TargetQueue = "orders";
    })
    .AddNexJobJobs(typeof(Program).Assembly);

var app = builder.Build();

// Ensure the RabbitMQ queue exists before consuming
using (var scope = app.Services.CreateScope())
{
    try
    {
        var factory = scope.ServiceProvider.GetRequiredService<IConnectionFactory>();
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();
        channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not pre-declare RabbitMQ queue '{Queue}'. Ensure RabbitMQ is running.", queueName);
    }
}

// ── Outbox Producer Endpoint ──────────────────────────────────────────────────
// Publishes an order event through NexJob's resilient Outbox pipeline.
// Even if RabbitMQ is temporarily down, the job is persisted in storage and retried.
app.MapPost("/orders", async ([FromBody] OrderPlacedEvent order, IScheduler scheduler) =>
{
    var headers = new Dictionary<string, string>
    {
        ["nexjob.job_type"] = typeof(ProcessOrderJob).AssemblyQualifiedName!,
    };

    var jobId = await scheduler.EnqueueRabbitMqAsync(
        exchange: string.Empty,
        routingKey: queueName,
        value: order,
        headers: headers,
        messageId: order.OrderId.ToString());

    return Results.Accepted($"/orders/{jobId.Value}/status", new
    {
        outboxJobId = jobId.Value,
        orderId = order.OrderId,
        status = "EnqueuedInOutbox",
        message = "Order will be reliably published to RabbitMQ via NexJob Outbox.",
    });
});

// ── Status Endpoint ───────────────────────────────────────────────────────────
app.MapGet("/orders/{jobId:guid}/status", async (Guid jobId, IDashboardStorage storage) =>
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

// ── Models & Jobs ─────────────────────────────────────────────────────────────

public record OrderPlacedEvent(Guid OrderId, string CustomerEmail, decimal Amount, DateTimeOffset PlacedAt);

public sealed class ProcessOrderJob(ILogger<ProcessOrderJob> logger) : IJob<string>
{
    public Task ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        logger.LogInformation("📦 [RabbitMQ Trigger] Processing received order message: {Payload}", input);
        return Task.CompletedTask;
    }
}
