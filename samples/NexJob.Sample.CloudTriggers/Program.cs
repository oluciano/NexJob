using System.Text.Json;
using NexJob;
using NexJob.Dashboard;
using NexJob.Trigger.AwsSqs;
using NexJob.Trigger.AzureServiceBus;
using NexJob.Trigger.GooglePubSub;
using NexJob.Trigger.Salesforce;
using NexJob.Trigger.SalesforceStreaming;

var builder = WebApplication.CreateBuilder(args);

// Register NexJob core with default in-memory storage and register all jobs in this assembly
builder.Services.AddNexJob(builder.Configuration)
    .AddNexJobJobs(typeof(Program).Assembly);

// Track which cloud triggers are enabled based on actual configuration (non-placeholder)
var activeTriggers = new List<string>();

// 1. AWS SQS Trigger
var sqsQueueUrl = builder.Configuration["NexJob:Triggers:AwsSqs:QueueUrl"];
if (IsValidConfig(sqsQueueUrl))
{
    builder.Services.AddNexJobAwsSqsTrigger(options =>
    {
        options.QueueUrl = sqsQueueUrl!;
        options.JobName = typeof(ProcessSqsOrderJob).AssemblyQualifiedName!;
        options.MaxMessages = builder.Configuration.GetValue("NexJob:Triggers:AwsSqs:MaxMessages", 10);
        options.WaitTimeSeconds = builder.Configuration.GetValue("NexJob:Triggers:AwsSqs:WaitTimeSeconds", 20);
        options.VisibilityTimeoutSeconds = builder.Configuration.GetValue("NexJob:Triggers:AwsSqs:VisibilityTimeoutSeconds", 30);
    });
    activeTriggers.Add("AWS SQS");
}

// 2. Azure Service Bus Trigger
var asbConn = builder.Configuration["NexJob:Triggers:AzureServiceBus:ConnectionString"];
if (IsValidConfig(asbConn))
{
    builder.Services.AddNexJobAzureServiceBusTrigger(options =>
    {
        options.ConnectionString = asbConn!;
        options.QueueOrTopicName = builder.Configuration["NexJob:Triggers:AzureServiceBus:QueueOrTopicName"] ?? "orders";
        options.SubscriptionName = builder.Configuration["NexJob:Triggers:AzureServiceBus:SubscriptionName"];
        options.MaxConcurrentMessages = builder.Configuration.GetValue("NexJob:Triggers:AzureServiceBus:MaxConcurrentMessages", 5);
    });
    activeTriggers.Add("Azure Service Bus");
}

// 3. Google Cloud Pub/Sub Trigger
var gcpProject = builder.Configuration["NexJob:Triggers:GooglePubSub:ProjectId"];
if (IsValidConfig(gcpProject))
{
    builder.Services.AddNexJobGooglePubSubTrigger(options =>
    {
        options.ProjectId = gcpProject!;
        options.SubscriptionId = builder.Configuration["NexJob:Triggers:GooglePubSub:SubscriptionId"] ?? "order-events-sub";
    });
    activeTriggers.Add("Google Cloud Pub/Sub");
}

// 4. Salesforce Pub/Sub API (gRPC CDC & Platform Events) Trigger
var sfTopic = builder.Configuration["NexJob:Triggers:Salesforce:Topic"];
var sfClientId = builder.Configuration["NexJob:Triggers:Salesforce:ClientId"];
var sfClientSecret = builder.Configuration["NexJob:Triggers:Salesforce:ClientSecret"];
if (IsValidConfig(sfTopic) && IsValidConfig(sfClientId) && IsValidConfig(sfClientSecret))
{
    builder.Services.AddSalesforceTrigger<ProcessSalesforceJob>(options =>
    {
        options.Topic = sfTopic!;
        options.ClientId = sfClientId!;
        options.ClientSecret = sfClientSecret!;
        var authEndpoint = builder.Configuration["NexJob:Triggers:Salesforce:AuthEndpoint"];
        if (IsValidConfig(authEndpoint))
        {
            options.AuthEndpoint = authEndpoint!;
        }
    });
    activeTriggers.Add("Salesforce Pub/Sub (gRPC)");
}

// 5. Salesforce Streaming API (CometD/Bayeux) Trigger
var sfChannel = builder.Configuration["NexJob:Triggers:SalesforceStreaming:Channel"];
var sfStreamClientId = builder.Configuration["NexJob:Triggers:SalesforceStreaming:ClientId"];
var sfStreamClientSecret = builder.Configuration["NexJob:Triggers:SalesforceStreaming:ClientSecret"];
var sfUsername = builder.Configuration["NexJob:Triggers:SalesforceStreaming:Username"];
var sfPassword = builder.Configuration["NexJob:Triggers:SalesforceStreaming:Password"];

if (IsValidConfig(sfChannel) && IsValidConfig(sfStreamClientId) && IsValidConfig(sfStreamClientSecret) &&
    IsValidConfig(sfUsername) && IsValidConfig(sfPassword))
{
    builder.Services.AddNexJobSalesforceStreamingTrigger<ProcessSalesforceStreamingJob>(options =>
    {
        options.Channel = sfChannel!;
        options.Authentication = new SalesforceStreamingAuthOptions
        {
            AuthType = SalesforceStreamingAuthType.OAuth2UsernamePassword,
            ClientId = sfStreamClientId,
            ClientSecret = sfStreamClientSecret,
            Username = sfUsername,
            Password = sfPassword,
        };
    });
    activeTriggers.Add("Salesforce Streaming (CometD)");
}

var app = builder.Build();

app.UseNexJobDashboard("/dashboard");

// Root information endpoint
app.MapGet("/", () => Results.Ok(new
{
    name = "NexJob Cloud Triggers Unified Sample",
    dashboard = "/dashboard",
    activeLiveTriggers = activeTriggers,
    hint = activeTriggers.Count == 0
        ? "No cloud triggers are currently connected. Configure credentials in appsettings.json or use /simulate/* endpoints to test each trigger's job handler."
        : $"{activeTriggers.Count} cloud triggers actively listening.",
    simulationEndpoints = new[]
    {
        "POST /simulate/sqs                   -> Simulates AWS SQS order message",
        "POST /simulate/azuresb               -> Simulates Azure Service Bus message",
        "POST /simulate/pubsub                -> Simulates GCP Pub/Sub message",
        "POST /simulate/salesforce            -> Simulates Salesforce Pub/Sub CDC event",
        "POST /simulate/salesforce-streaming  -> Simulates Salesforce Streaming event",
    },
}));

app.MapGet("/triggers", () => Results.Ok(new
{
    activeTriggers,
    allSupported = new[]
    {
        "AWS SQS (NexJob.Trigger.AwsSqs)",
        "Azure Service Bus (NexJob.Trigger.AzureServiceBus)",
        "Google Cloud Pub/Sub (NexJob.Trigger.GooglePubSub)",
        "Salesforce Pub/Sub gRPC (NexJob.Trigger.Salesforce)",
        "Salesforce Streaming CometD (NexJob.Trigger.SalesforceStreaming)",
    },
}));

// Simulation endpoints for local testing without cloud infrastructure
app.MapPost("/simulate/sqs", async (IScheduler scheduler) =>
{
    var samplePayload = JsonSerializer.Serialize(new
    {
        orderId = $"ORD-SQS-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}",
        source = "AWS SQS Trigger",
        amount = 129.99,
        timestamp = DateTimeOffset.UtcNow,
    });

    var jobId = await scheduler.EnqueueAsync<ProcessSqsOrderJob, string>(samplePayload);
    return Results.Accepted("/dashboard", new { jobId = jobId.Value, simulatedTrigger = "AWS SQS", payload = samplePayload });
});

app.MapPost("/simulate/azuresb", async (IScheduler scheduler) =>
{
    var samplePayload = JsonSerializer.Serialize(new
    {
        messageId = $"ASB-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}",
        source = "Azure Service Bus Trigger",
        topic = "orders",
        timestamp = DateTimeOffset.UtcNow,
    });

    var jobId = await scheduler.EnqueueAsync<ProcessAzureMessageJob, string>(samplePayload);
    return Results.Accepted("/dashboard", new { jobId = jobId.Value, simulatedTrigger = "Azure Service Bus", payload = samplePayload });
});

app.MapPost("/simulate/pubsub", async (IScheduler scheduler) =>
{
    var samplePayload = JsonSerializer.Serialize(new
    {
        eventId = $"GCP-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}",
        source = "Google Cloud Pub/Sub Trigger",
        subscription = "order-events-sub",
        timestamp = DateTimeOffset.UtcNow,
    });

    var jobId = await scheduler.EnqueueAsync<ProcessPubSubEventJob, string>(samplePayload);
    return Results.Accepted("/dashboard", new { jobId = jobId.Value, simulatedTrigger = "Google Pub/Sub", payload = samplePayload });
});

app.MapPost("/simulate/salesforce", async (IScheduler scheduler) =>
{
    var samplePayload = JsonSerializer.Serialize(new
    {
        changeType = "UPDATE",
        entityName = "Account",
        recordId = $"001{Guid.NewGuid().ToString()[..12].ToUpperInvariant()}",
        source = "Salesforce Pub/Sub (gRPC)",
        timestamp = DateTimeOffset.UtcNow,
    });

    var jobId = await scheduler.EnqueueAsync<ProcessSalesforceJob, string>(samplePayload);
    return Results.Accepted("/dashboard", new { jobId = jobId.Value, simulatedTrigger = "Salesforce Pub/Sub", payload = samplePayload });
});

app.MapPost("/simulate/salesforce-streaming", async (IScheduler scheduler) =>
{
    using var doc = JsonDocument.Parse("{\"sobject\":{\"Id\":\"003XX0000123456\",\"LastName\":\"Doe\",\"FirstName\":\"John\"}}");
    var input = new SalesforceStreamingEventInput(
        ReplayId: 10452,
        Channel: "/data/ContactChangeEvent",
        Payload: doc.RootElement.Clone(),
        CreatedDate: DateTimeOffset.UtcNow,
        EventId: Guid.NewGuid().ToString());

    var jobId = await scheduler.EnqueueAsync<ProcessSalesforceStreamingJob, SalesforceStreamingEventInput>(input);
    return Results.Accepted("/dashboard", new { jobId = jobId.Value, simulatedTrigger = "Salesforce Streaming", input });
});

await app.RunAsync();

static bool IsValidConfig(string? value) =>
    !string.IsNullOrWhiteSpace(value) && !value.StartsWith('<');

// ─── Job Definitions for Cloud Triggers ───────────────────────────────────────

/// <summary>
/// Handles messages delivered via AWS SQS trigger.
/// </summary>
public sealed class ProcessSqsOrderJob : IJob<string>
{
    private readonly ILogger<ProcessSqsOrderJob> _logger;

    public ProcessSqsOrderJob(ILogger<ProcessSqsOrderJob> logger) => _logger = logger;

    public Task ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("📦 [AWS SQS Trigger Job] Received message body: {Body}", input);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Handles messages delivered via Azure Service Bus trigger.
/// </summary>
public sealed class ProcessAzureMessageJob : IJob<string>
{
    private readonly ILogger<ProcessAzureMessageJob> _logger;

    public ProcessAzureMessageJob(ILogger<ProcessAzureMessageJob> logger) => _logger = logger;

    public Task ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("☁️ [Azure Service Bus Trigger Job] Received message body: {Body}", input);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Handles events delivered via Google Cloud Pub/Sub trigger.
/// </summary>
public sealed class ProcessPubSubEventJob : IJob<string>
{
    private readonly ILogger<ProcessPubSubEventJob> _logger;

    public ProcessPubSubEventJob(ILogger<ProcessPubSubEventJob> logger) => _logger = logger;

    public Task ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("🌐 [Google Pub/Sub Trigger Job] Received event payload: {Payload}", input);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Handles CDC & Platform Events delivered via Salesforce gRPC Pub/Sub trigger.
/// </summary>
public sealed class ProcessSalesforceJob : IJob<string>
{
    private readonly ILogger<ProcessSalesforceJob> _logger;

    public ProcessSalesforceJob(ILogger<ProcessSalesforceJob> logger) => _logger = logger;

    public Task ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("⚡ [Salesforce Pub/Sub Trigger Job] Received CDC event: {Event}", input);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Handles events delivered via Salesforce Streaming (CometD/Bayeux) trigger.
/// </summary>
public sealed class ProcessSalesforceStreamingJob : IJob<SalesforceStreamingEventInput>
{
    private readonly ILogger<ProcessSalesforceStreamingJob> _logger;

    public ProcessSalesforceStreamingJob(ILogger<ProcessSalesforceStreamingJob> logger) => _logger = logger;

    public Task ExecuteAsync(SalesforceStreamingEventInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "⚡ [Salesforce Streaming Trigger Job] Channel: {Channel}, ReplayId: {ReplayId}, EventId: {EventId}, Payload: {Payload}",
            input.Channel,
            input.ReplayId,
            input.EventId,
            input.GetRawJson());
        return Task.CompletedTask;
    }
}
