using System.Reflection;
using NexJob;
using NexJob.Dashboard;
using NexJob.Sample.WebApi.Jobs;
using NexJob.Storage;

var builder = WebApplication.CreateBuilder(args);

// Load all NexJob settings from appsettings.json "NexJob" section
builder.Services.AddNexJob(builder.Configuration, opt =>
{
    opt.PollingInterval = TimeSpan.FromMilliseconds(200); // fast polling for demo
    opt.MaxAttempts = 1; // fail fast for demo (dead-letters immediately)
});

builder.Services.AddNexJobJobs(typeof(SendEmailJob).Assembly);

var app = builder.Build();

// ── Register recurring jobs on startup ───────────────────────────────────────

app.Lifetime.ApplicationStarted.Register(() =>
{
    using var scope = app.Services.CreateScope();
    var scheduler = scope.ServiceProvider.GetRequiredService<IScheduler>();

    scheduler.RecurringAsync<CleanupJob, CleanupRequest>(
        "nightly-cleanup",
        new CleanupRequest("temp-files", RetentionDays: 30),
        cron: "0 2 * * *").GetAwaiter().GetResult();

    scheduler.RecurringAsync<GenerateReportJob, ReportRequest>(
        "daily-report",
        new ReportRequest("daily",
            DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
            DateOnly.FromDateTime(DateTime.Today)),
        cron: "0 6 * * *",
        queue: "reports").GetAwaiter().GetResult();

    scheduler.RecurringAsync<SendEmailJob, EmailPayload>(
        "weekly-newsletter",
        new EmailPayload("newsletter@nexjob.dev", "Weekly digest", string.Empty),
        cron: "0 9 * * 1").GetAwaiter().GetResult();
});

// ── Fire-and-forget ───────────────────────────────────────────────────────────

app.MapPost("/jobs/email", async (EmailPayload payload, IScheduler scheduler) =>
{
    var id = await scheduler.EnqueueAsync<SendEmailJob, EmailPayload>(payload);
    return Results.Accepted($"/jobs/{id.Value}/status", new { jobId = id.Value });
});

app.MapPost("/jobs/report", async (ReportRequest payload, IScheduler scheduler) =>
{
    var id = await scheduler.EnqueueAsync<GenerateReportJob, ReportRequest>(payload, queue: "reports");
    return Results.Accepted($"/jobs/{id.Value}/status", new { jobId = id.Value });
});

// ── Bulk email ────────────────────────────────────────────────────────────────

app.MapPost("/jobs/email/bulk", async (BulkEmailPayload payload, IScheduler scheduler) =>
{
    var id = await scheduler.EnqueueAsync<BulkEmailJob, BulkEmailPayload>(payload, queue: "bulk");
    return Results.Accepted($"/jobs/{id.Value}/status", new { jobId = id.Value });
});

// ── Orders domain ─────────────────────────────────────────────────────────────

app.MapPost("/orders/{orderId}/confirm-email", async (string orderId, IScheduler scheduler) =>
{
    var payload = new EmailPayload($"customer-{orderId}@example.com", $"Order {orderId} Confirmed", "Thank you for your order.");
    var id = await scheduler.EnqueueAsync<SendEmailJob, EmailPayload>(payload, priority: JobPriority.High);
    return Results.Accepted($"/jobs/{id.Value}/status", new { jobId = id.Value, orderId });
});

app.MapPost("/orders/bulk-invoice", async (BulkInvoiceRequest req, IScheduler scheduler) =>
{
    var payload = new BulkEmailPayload(req.OrderIds.Select(o => $"{o}@example.com").ToArray(), "Your Invoice", "Please find your invoice attached.");
    var id = await scheduler.EnqueueAsync<BulkEmailJob, BulkEmailPayload>(payload, queue: "bulk");
    return Results.Accepted($"/jobs/{id.Value}/status", new { jobId = id.Value, orderCount = req.OrderIds.Length });
});

// ── Campaigns domain ──────────────────────────────────────────────────────────

app.MapPost("/campaigns/{campaignId}/send", async (string campaignId, CampaignSendRequest req, IScheduler scheduler) =>
{
    var payload = new BulkEmailPayload(req.Recipients, $"Campaign: {campaignId}", req.Body);
    var id = await scheduler.EnqueueAsync<BulkEmailJob, BulkEmailPayload>(payload, queue: "bulk");
    return Results.Accepted($"/jobs/{id.Value}/status", new { jobId = id.Value, campaignId, recipientCount = req.Recipients.Length });
});

app.MapPost("/campaigns/{campaignId}/schedule", async (string campaignId, ScheduledCampaignRequest req, IScheduler scheduler) =>
{
    var payload = new BulkEmailPayload(req.Recipients, $"Campaign: {campaignId}", req.Body);
    var id = await scheduler.ScheduleAtAsync<BulkEmailJob, BulkEmailPayload>(payload, req.SendAt, queue: "bulk");
    return Results.Accepted($"/jobs/{id.Value}/status", new { jobId = id.Value, campaignId, scheduledFor = req.SendAt });
});

// ── Job status lookup ─────────────────────────────────────────────────────────

app.MapGet("/jobs/{id:guid}/status", async (Guid id, IStorageProvider storage) =>
{
    var job = await storage.GetJobByIdAsync(new JobId(id));
    if (job is null)
    {
        return Results.NotFound(new { error = "Job not found", id });
    }

    return Results.Ok(new
    {
        job.Id.Value,
        Status = job.Status.ToString(),
        job.Queue,
        Priority = job.Priority.ToString(),
        job.Attempts,
        job.MaxAttempts,
        job.CreatedAt,
        job.ScheduledAt,
        job.CompletedAt,
        LastErrorMessage = job.LastErrorMessage,
    });
});

// ── Scheduled (delayed) ───────────────────────────────────────────────────────

app.MapPost("/jobs/report/schedule", async (ScheduleReportRequest req, IScheduler scheduler) =>
{
    var id = await scheduler.ScheduleAsync<GenerateReportJob, ReportRequest>(
        req.Report, TimeSpan.FromSeconds(req.DelaySeconds), queue: "reports");
    return Results.Accepted($"/jobs/{id.Value}/status", new { jobId = id.Value, delaySeconds = req.DelaySeconds });
});

// ── Recurring management ──────────────────────────────────────────────────────

app.MapPost("/jobs/cleanup/recurring", async (IScheduler scheduler) =>
{
    await scheduler.RecurringAsync<CleanupJob, CleanupRequest>(
        recurringJobId: "nightly-cleanup",
        input: new CleanupRequest("temp-files", RetentionDays: 30),
        cron: "0 2 * * *");
    return Results.Ok(new { recurringJobId = "nightly-cleanup", cron = "0 2 * * *" });
});

app.MapDelete("/jobs/cleanup/recurring", async (IScheduler scheduler) =>
{
    await scheduler.RemoveRecurringAsync("nightly-cleanup");
    return Results.NoContent();
});

app.MapPost("/jobs/seed/recurring", async (IScheduler scheduler) =>
{
    await scheduler.RecurringAsync<CleanupJob, CleanupRequest>(
        "hourly-cleanup", new CleanupRequest("cache", 1), "0 * * * *");
    await scheduler.RecurringAsync<CleanupJob, CleanupRequest>(
        "weekly-archive", new CleanupRequest("archive", 90), "0 3 * * 0");
    await scheduler.RecurringAsync<GenerateReportJob, ReportRequest>(
        "daily-report",
        new ReportRequest("daily",
            DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
            DateOnly.FromDateTime(DateTime.Today)),
        "0 6 * * *",
        queue: "reports");
    await scheduler.RecurringAsync<SendEmailJob, EmailPayload>(
        "weekly-newsletter", new EmailPayload("newsletter@nexjob.dev", "Weekly digest", string.Empty), "0 9 * * 1");
    return Results.Ok(new { registered = 4 });
});

app.MapPost("/jobs/seed/failed", async (IScheduler scheduler) =>
{
    for (var i = 1; i <= 5; i++)
    {
        await scheduler.EnqueueAsync<FlakeyJob, FlakeyRequest>(
            new FlakeyRequest($"dead-letter-{i}", FailTimes: 99));
    }

    return Results.Accepted(null, new { enqueued = 5, note = "will fail after max attempts" });
});

// ── Chain (continuation) ──────────────────────────────────────────────────────

app.MapPost("/jobs/chain", async (EmailPayload email, IScheduler scheduler) =>
{
    var report = new ReportRequest("sales-summary",
        DateOnly.FromDateTime(DateTime.Today.AddDays(-7)),
        DateOnly.FromDateTime(DateTime.Today));
    var reportId = await scheduler.EnqueueAsync<GenerateReportJob, ReportRequest>(report, queue: "reports");
    var emailId = await scheduler.ContinueWithAsync<SendEmailJob, EmailPayload>(reportId, email);

    return Results.Accepted(null, new
    {
        reportJobId = reportId.Value,
        emailJobId = emailId.Value,
        description = "Email will be sent after the report finishes",
    });
});

// ── Priority demo ─────────────────────────────────────────────────────────────

app.MapPost("/jobs/email/priority", async (PriorityEmailRequest req, IScheduler scheduler) =>
{
    var id = await scheduler.EnqueueAsync<SendEmailJob, EmailPayload>(req.Email, priority: req.Priority);
    return Results.Accepted($"/jobs/{id.Value}/status", new { jobId = id.Value, priority = req.Priority.ToString() });
});

// ── Idempotency demo ──────────────────────────────────────────────────────────

app.MapPost("/jobs/email/idempotent", async (IdempotentEmailRequest req, IScheduler scheduler) =>
{
    var id = await scheduler.EnqueueAsync<SendEmailJob, EmailPayload>(req.Email, idempotencyKey: req.IdempotencyKey);
    return Results.Accepted($"/jobs/{id.Value}/status", new { jobId = id.Value, idempotencyKey = req.IdempotencyKey });
});

// ── Retry stress ──────────────────────────────────────────────────────────────

app.MapPost("/jobs/flakey", async (FlakeyRequest payload, IScheduler scheduler) =>
{
    var id = await scheduler.EnqueueAsync<FlakeyJob, FlakeyRequest>(payload);
    return Results.Accepted($"/jobs/{id.Value}/status", new { jobId = id.Value });
});

// ── Worker-pool saturation ────────────────────────────────────────────────────

app.MapPost("/jobs/slow", async (SlowRequest payload, IScheduler scheduler) =>
{
    var id = await scheduler.EnqueueAsync<SlowJob, SlowRequest>(payload);
    return Results.Accepted($"/jobs/{id.Value}/status", new { jobId = id.Value });
});

// ── STRESS: enqueue N jobs of mixed types in one call ────────────────────────

app.MapPost("/jobs/stress", async (StressRequest req, IScheduler scheduler) =>
{
    var ids = new List<object>();
    var rng = new Random();

    for (int i = 0; i < req.Count; i++)
    {
        var priority = (JobPriority)rng.Next(1, 5);

        var jobId = (i % 4) switch
        {
            0 => await scheduler.EnqueueAsync<SendEmailJob, EmailPayload>(
                    new EmailPayload($"user{i}@test.com", $"Subject {i}", "body"),
                    priority: priority),
            1 => await scheduler.EnqueueAsync<GenerateReportJob, ReportRequest>(
                    new ReportRequest($"report-{i}",
                        DateOnly.FromDateTime(DateTime.Today.AddDays(-7)),
                        DateOnly.FromDateTime(DateTime.Today)),
                    priority: priority,
                    queue: "reports"),
            2 => await scheduler.EnqueueAsync<FlakeyJob, FlakeyRequest>(
                    new FlakeyRequest($"flakey-{i}", FailTimes: rng.Next(0, 3)),
                    priority: priority),
            _ => await scheduler.EnqueueAsync<SlowJob, SlowRequest>(
                    new SlowRequest($"slow-{i}", DurationMs: rng.Next(100, 500)),
                    priority: priority),
        };

        ids.Add(new { jobId = jobId.Value, priority = priority.ToString() });
    }

    return Results.Accepted(null, new { enqueued = ids.Count, jobs = ids });
});

// ── STRESS: idempotency under concurrent load ─────────────────────────────────

app.MapPost("/jobs/stress/idempotent", async (IdempotentStressRequest req, IScheduler scheduler) =>
{
    var tasks = Enumerable.Range(0, req.Concurrency)
        .Select(_ => scheduler.EnqueueAsync<SendEmailJob, EmailPayload>(
            new EmailPayload("stress@test.com", "idem-subject", "body"),
            idempotencyKey: req.Key));

    var results = await Task.WhenAll(tasks);
    var distinctIds = results.Select(r => r.Value).Distinct().ToList();

    return Results.Ok(new
    {
        requestsSent = req.Concurrency,
        distinctJobIds = distinctIds.Count,
        ids = distinctIds,
    });
});

// ── Status ────────────────────────────────────────────────────────────────────

app.MapGet("/jobs/due-recurring", async (IStorageProvider storage) =>
{
    var due = await storage.GetDueRecurringJobsAsync(DateTimeOffset.UtcNow.AddYears(1));
    return Results.Ok(due.Select(r => new
    {
        r.RecurringJobId,
        r.Cron,
        r.Queue,
        r.NextExecution,
        r.LastExecutedAt,
    }));
});

app.UseNexJobDashboard("/dashboard");

await app.RunAsync();

// ── Request models ────────────────────────────────────────────────────────────

record ScheduleReportRequest(ReportRequest Report, int DelaySeconds);
record PriorityEmailRequest(EmailPayload Email, JobPriority Priority);
record IdempotentEmailRequest(EmailPayload Email, string IdempotencyKey);
record StressRequest(int Count);
record IdempotentStressRequest(string Key, int Concurrency);
record BulkInvoiceRequest(string[] OrderIds);
record CampaignSendRequest(string[] Recipients, string Body);
record ScheduledCampaignRequest(string[] Recipients, string Body, DateTimeOffset SendAt);
