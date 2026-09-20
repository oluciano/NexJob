using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using NexJob;
using NexJob.Dashboard;
using NexJob.SqlServer;
using NexJob.Storage;

var builder = WebApplication.CreateBuilder(args);

var bootstrapServers = builder.Configuration.GetValue("Kafka:BootstrapServers", "localhost:9092")!;
var topicName = builder.Configuration.GetValue("Kafka:Topic", "customer-registrations")!;
var groupId = builder.Configuration.GetValue("Kafka:GroupId", "nexjob-customer-consumer-group")!;
var sqlServerConn = builder.Configuration.GetConnectionString("SqlServer");

// 1. Configure Storage: Use SQL Server if connection string is configured, otherwise fallback to InMemory
if (!string.IsNullOrWhiteSpace(sqlServerConn))
{
    builder.Services.AddNexJobSqlServer(sqlServerConn);
}

// 2. Configure NexJob Engine with Kafka Producer & Consumer Trigger
builder.Services.AddNexJob(opt =>
    {
        opt.Workers = 5;
        opt.Queues = ["default", "customers-queue"];
        opt.PollingInterval = TimeSpan.FromMilliseconds(50);
    })
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
        opt.TargetQueue = "customers-queue";
    })
    .AddNexJobJobs(typeof(Program).Assembly);

var app = builder.Build();

// 3. Database Initialization: Ensure the Customers business table exists in SQL Server
if (!string.IsNullOrWhiteSpace(sqlServerConn))
{
    try
    {
        await using var conn = new SqlConnection(sqlServerConn);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Customers')
            BEGIN
                CREATE TABLE Customers (
                    CustomerId UNIQUEIDENTIFIER PRIMARY KEY,
                    Document NVARCHAR(50) NOT NULL,
                    Name NVARCHAR(200) NOT NULL,
                    Email NVARCHAR(200) NOT NULL,
                    RegisteredAt DATETIMEOFFSET NOT NULL,
                    ProcessedByNode NVARCHAR(100) NOT NULL,
                    UpdatedAt DATETIMEOFFSET NOT NULL
                );
            END
            """);
        app.Logger.LogInformation("SQL Server business table 'Customers' verified/created successfully.");
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not initialize Customers table in SQL Server. Ensure container is running.");
    }
}

// ── Root Endpoint ─────────────────────────────────────────────────────────────
app.MapGet("/", () => Results.Ok(new
{
    service = "NexJob Kafka + SQL Server Sample",
    dashboard = "/dashboard",
    kafkaTopic = topicName,
    consumerGroup = groupId,
    endpoints = new[]
    {
        "POST /customers/bulk?count=10 -> Directly publishes customers to Kafka (bypassing NexJob)",
        "GET  /customers              -> Queries customers saved in SQL Server",
        "POST /events                 -> Publishes event via NexJob Outbox Producer",
        "GET  /events/{jobId}/status  -> Checks Outbox event delivery status",
        "GET  /dashboard              -> NexJob Web Dashboard",
    },
}));

// ── Simulator: Pure Kafka Producer (Bypassing NexJob) ──────────────────────────
// Simulates an external microservice or CRM publishing customer registrations directly into Kafka.
app.MapPost("/customers/bulk", async ([FromQuery] int count = 10) =>
{
    var safeCount = Math.Clamp(count, 1, 500);
    var producerConfig = new ProducerConfig
    {
        BootstrapServers = bootstrapServers,
        Acks = Acks.All,
    };

    using var producer = new ProducerBuilder<string, string>(producerConfig).Build();
    var producedCustomers = new List<CustomerPayload>();

    for (var i = 1; i <= safeCount; i++)
    {
        var customer = new CustomerPayload(
            CustomerId: Guid.NewGuid(),
            Document: $"{Random.Shared.Next(100, 999)}.{Random.Shared.Next(100, 999)}.{Random.Shared.Next(100, 999)}-{Random.Shared.Next(10, 99)}",
            Name: $"Customer {Guid.NewGuid().ToString()[..6].ToUpperInvariant()}",
            Email: $"customer_{Guid.NewGuid().ToString()[..6]}@company.com",
            RegisteredAt: DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(customer);
        var headers = new Headers
        {
            { "nexjob.job_type", Encoding.UTF8.GetBytes(typeof(SaveCustomerJob).AssemblyQualifiedName!) },
        };

        await producer.ProduceAsync(topicName, new Message<string, string>
        {
            Key = customer.CustomerId.ToString(),
            Value = JsonSerializer.Serialize(json),
            Headers = headers,
        });

        producedCustomers.Add(customer);
    }

    producer.Flush(TimeSpan.FromSeconds(5));

    return Results.Ok(new
    {
        message = $"Directly produced {safeCount} customer messages into Kafka topic '{topicName}' (bypassing NexJob).",
        topic = topicName,
        count = safeCount,
        hint = "NexJob Kafka Trigger will consume these messages, commit offsets, and invoke SaveCustomerJob to save them into SQL Server.",
        sample = producedCustomers.FirstOrDefault(),
    });
});

// ── Query Customers in SQL Server ─────────────────────────────────────────────
app.MapGet("/customers", async () =>
{
    if (string.IsNullOrWhiteSpace(sqlServerConn))
    {
        return Results.Ok(new { message = "SQL Server connection string not configured." });
    }

    await using var conn = new SqlConnection(sqlServerConn);
    var records = (await conn.QueryAsync<CustomerRecord>(
        "SELECT TOP 50 CustomerId, Document, Name, Email, RegisteredAt, ProcessedByNode, UpdatedAt FROM Customers ORDER BY UpdatedAt DESC")).ToList();

    return Results.Ok(new
    {
        totalQueried = records.Count,
        customers = records,
    });
});

// ── Outbox Producer Endpoint ──────────────────────────────────────────────────
app.MapPost("/events", async ([FromBody] UserEventPayload payload, IScheduler scheduler) =>
{
    var headers = new Dictionary<string, string>
    {
        ["nexjob.job_type"] = typeof(ProcessUserEventJob).AssemblyQualifiedName!,
    };

    var jobId = await scheduler.EnqueueKafkaAsync(
        topic: "user-events",
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

// ── Models & Consumer Jobs ────────────────────────────────────────────────────

public record CustomerPayload(
    Guid CustomerId,
    string Document,
    string Name,
    string Email,
    DateTimeOffset RegisteredAt);

public record CustomerRecord(
    Guid CustomerId,
    string Document,
    string Name,
    string Email,
    DateTimeOffset RegisteredAt,
    string ProcessedByNode,
    DateTimeOffset UpdatedAt);

public sealed class SaveCustomerJob(
    IConfiguration configuration,
    ILogger<SaveCustomerJob> logger) : IJob<string>
{
    public async Task ExecuteAsync(string rawJson, CancellationToken cancellationToken)
    {
        var customer = JsonSerializer.Deserialize<CustomerPayload>(rawJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidOperationException("Invalid customer payload JSON.");

        var sqlServerConn = configuration.GetConnectionString("SqlServer");
        if (string.IsNullOrWhiteSpace(sqlServerConn))
        {
            logger.LogWarning("SQL Server not configured. Skipping persistence for {CustomerId}.", customer.CustomerId);
            return;
        }

        await using var connection = new SqlConnection(sqlServerConn);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            MERGE INTO Customers AS target
            USING (SELECT @CustomerId AS CustomerId, @Document AS Document, @Name AS Name, @Email AS Email, @RegisteredAt AS RegisteredAt, @ProcessedByNode AS ProcessedByNode) AS source
            ON target.CustomerId = source.CustomerId
            WHEN MATCHED THEN
                UPDATE SET Name = source.Name, Email = source.Email, ProcessedByNode = source.ProcessedByNode, UpdatedAt = SYSDATETIMEOFFSET()
            WHEN NOT MATCHED THEN
                INSERT (CustomerId, Document, Name, Email, RegisteredAt, ProcessedByNode, UpdatedAt)
                VALUES (source.CustomerId, source.Document, source.Name, source.Email, source.RegisteredAt, source.ProcessedByNode, SYSDATETIMEOFFSET());
            """;

        var nodeName = $"{Environment.MachineName}:{Environment.ProcessId}";
        var param = new
        {
            customer.CustomerId,
            customer.Document,
            customer.Name,
            customer.Email,
            customer.RegisteredAt,
            ProcessedByNode = nodeName,
        };

        await connection.ExecuteAsync(new CommandDefinition(sql, param, cancellationToken: cancellationToken));
        logger.LogInformation("💾 [NexJob SaveCustomerJob] Successfully saved customer {CustomerId} ({Name}) to SQL Server by node {Node}.", customer.CustomerId, customer.Name, nodeName);
    }
}

public record UserEventPayload(Guid EventId, string UserId, string EventType, string Details, DateTimeOffset Timestamp);

public sealed class ProcessUserEventJob(ILogger<ProcessUserEventJob> logger) : IJob<string>
{
    public Task ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        logger.LogInformation("⚡ [Kafka Trigger] Received and processed user event: {Payload}", input);
        return Task.CompletedTask;
    }
}
