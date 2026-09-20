using System.Diagnostics;
using NexJob;
using NexJob.Dashboard;
using NexJob.OpenTelemetry;
using NexJob.Postgres;
using NexJob.Redis;
using NexJob.Storage;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Read configuration connection strings
var primaryConn = builder.Configuration.GetConnectionString("NexJobPostgresPrimary")
    ?? "Host=localhost;Port=5432;Database=nexjob_sample;Username=postgres;Password=postgres";
var replicaConn = builder.Configuration.GetConnectionString("NexJobPostgresReplica")
    ?? primaryConn;
var redisConn = builder.Configuration.GetConnectionString("NexJobRedis")
    ?? "localhost:6379";

// 1. OpenTelemetry Tracing & Metrics (Native NexJob Instrumentation)
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .AddNexJobInstrumentation()
            .AddConsoleExporter();
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddNexJobInstrumentation()
            .AddConsoleExporter();
    });

// 2. Custom Middleware Pipeline Filter (Timing & Audit)
builder.Services.AddSingleton<IJobExecutionFilter, TimingAndAuditFilter>();

// 3. Storage Provider Setup: PostgreSQL Primary
builder.Services.AddNexJobPostgres(primaryConn);

// 4. Redis Distributed Throttle Coordination
try
{
    var muxer = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
    {
        EndPoints = { redisConn },
        AbortOnConnectFail = false,
        ConnectTimeout = 3000,
    }).ConfigureAwait(false);
    builder.Services.AddSingleton<IConnectionMultiplexer>(muxer);
    builder.Services.AddSingleton(muxer.GetDatabase());
    builder.Services.AddNexJobDistributedThrottle();
}
catch (Exception ex)
{
    Console.WriteLine($"[Storage Sample] Warning: Could not connect to Redis ({ex.Message}). Distributed throttle will fall back to local process throttle.");
}

// 5. NexJob Core Setup + Read Replica Dashboard Storage
builder.Services.AddNexJob(builder.Configuration)
    .UseDashboardReadReplica(replicaConn)
    .AddNexJobJobs(typeof(Program).Assembly);

var app = builder.Build();

// Dashboard UI
app.UseNexJobDashboard("/dashboard");

// Enqueue a batch of payments to demonstrate distributed throttle (limited to 2 concurrent executions)
app.MapPost("/payments/batch", async (IScheduler scheduler, int count = 5) =>
{
    var jobIds = new List<JobId>();
    for (var i = 1; i <= count; i++)
    {
        var payment = new PaymentRequest(
            PaymentId: $"PAY-{Guid.NewGuid().ToString()[..8].ToUpperInvariant()}",
            Amount: 50.00m * i,
            Currency: "USD");

        var id = await scheduler.EnqueueAsync<ProcessPaymentJob, PaymentRequest>(payment);
        jobIds.Add(id);
    }

    return Results.Accepted("/dashboard", new
    {
        message = $"Enqueued {count} payments. Throttle limit is 2 concurrent executions across all nodes.",
        jobIds = jobIds.Select(id => id.Value),
    });
});

// Enqueue a single payment
app.MapPost("/payments", async (PaymentRequest payment, IScheduler scheduler) =>
{
    var id = await scheduler.EnqueueAsync<ProcessPaymentJob, PaymentRequest>(payment);
    return Results.Accepted($"/jobs/{id.Value}", new { jobId = id.Value, payment });
});

// Query jobs using segregated IDashboardStorage (executing on the read replica)
app.MapGet("/jobs", async (IDashboardStorage dashboardStorage) =>
{
    var metrics = await dashboardStorage.GetMetricsAsync();
    var pagedJobs = await dashboardStorage.GetJobsAsync(
        new JobFilter { Status = JobStatus.Processing },
        page: 1,
        pageSize: 10);

    return Results.Ok(new
    {
        StorageSource = "PostgreSQL Read Replica",
        Metrics = metrics,
        ProcessingJobs = pagedJobs.Items,
    });
});

app.MapGet("/jobs/{id:guid}", async (Guid id, IDashboardStorage dashboardStorage) =>
{
    var job = await dashboardStorage.GetJobByIdAsync(new JobId(id));
    return job is null ? Results.NotFound() : Results.Ok(job);
});

app.MapGet("/", () => Results.Ok(new
{
    name = "NexJob Storage & Observability Sample",
    dashboard = "/dashboard",
    endpoints = new[]
    {
        "POST /payments/batch?count=5  -> Enqueues payments demonstrating distributed throttle",
        "POST /payments                -> Enqueues a single payment",
        "GET  /jobs                    -> Queries jobs from read replica via IDashboardStorage",
        "GET  /dashboard               -> NexJob web dashboard",
    },
}));

await app.RunAsync();

// ─── Models & Jobs ────────────────────────────────────────────────────────────

public sealed record PaymentRequest(string PaymentId, decimal Amount, string Currency);

/// <summary>
/// Throttled job: only 2 payments can execute concurrently across the entire cluster.
/// </summary>
[Throttle("payment-gateway", 2)]
public sealed class ProcessPaymentJob : IJob<PaymentRequest>
{
    private readonly ILogger<ProcessPaymentJob> _logger;

    public ProcessPaymentJob(ILogger<ProcessPaymentJob> logger)
    {
        _logger = logger;
    }

    public async Task ExecuteAsync(PaymentRequest input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("💳 [Throttle: 2 max] Processing payment {PaymentId} for {Amount} {Currency}...",
            input.PaymentId, input.Amount, input.Currency);

        // Simulate gateway processing time
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("✅ Payment {PaymentId} authorized successfully.", input.PaymentId);
    }
}

/// <summary>
/// Demonstrates an enterprise cross-cutting filter for audit logging and performance metrics.
/// </summary>
public sealed class TimingAndAuditFilter : IJobExecutionFilter
{
    private readonly ILogger<TimingAndAuditFilter> _logger;

    public TimingAndAuditFilter(ILogger<TimingAndAuditFilter> logger)
    {
        _logger = logger;
    }

    public async Task OnExecutingAsync(JobExecutingContext context, JobExecutionDelegate next, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("➡️ [Pipeline Filter] Starting job {JobId} ({JobType})", context.Job.Id, context.Job.JobType);

        await next(ct).ConfigureAwait(false);

        sw.Stop();
        _logger.LogInformation("✅ [Pipeline Filter] Completed job {JobId} in {ElapsedMilliseconds}ms", context.Job.Id, sw.ElapsedMilliseconds);
    }
}
