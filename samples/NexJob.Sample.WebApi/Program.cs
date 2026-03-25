using NexJob;
using NexJob.Dashboard;
using NexJob.Sample.WebApi.Jobs;
using NexJob.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNexJob(opt =>
{
    opt.Workers = 10;
    opt.PollingInterval = TimeSpan.FromMilliseconds(200); // fast polling for demo
    opt.MaxAttempts = 1; // fail fast for demo (dead-letters immediately)
});

builder.Services.AddTransient<SendEmailJob>();
builder.Services.AddTransient<GenerateReportJob>();
builder.Services.AddTransient<CleanupJob>();
builder.Services.AddTransient<FlakeyJob>();
builder.Services.AddTransient<SlowJob>();

var app = builder.Build();

// ── Fire-and-forget ───────────────────────────────────────────────────────────

app.MapPost("/jobs/email", async (EmailPayload payload, IScheduler scheduler) =>
{
    var id = await scheduler.EnqueueAsync<SendEmailJob, EmailPayload>(payload);
    return Results.Accepted($"/jobs/{id.Value}", new { jobId = id.Value });
});

app.MapPost("/jobs/report", async (ReportRequest payload, IScheduler scheduler) =>
{
    var id = await scheduler.EnqueueAsync<GenerateReportJob, ReportRequest>(payload);
    return Results.Accepted($"/jobs/{id.Value}", new { jobId = id.Value });
});

// ── Scheduled (delayed) ───────────────────────────────────────────────────────

app.MapPost("/jobs/report/schedule", async (ScheduleReportRequest req, IScheduler scheduler) =>
{
    var id = await scheduler.ScheduleAsync<GenerateReportJob, ReportRequest>(
        req.Report, TimeSpan.FromSeconds(req.DelaySeconds));
    return Results.Accepted($"/jobs/{id.Value}", new { jobId = id.Value, delaySeconds = req.DelaySeconds });
});

// ── Recurring ─────────────────────────────────────────────────────────────────

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
        "daily-report", new ReportRequest("daily",
            DateOnly.FromDateTime(DateTime.Today.AddDays(-1)),
            DateOnly.FromDateTime(DateTime.Today)), "0 6 * * *");
    await scheduler.RecurringAsync<SendEmailJob, EmailPayload>(
        "weekly-newsletter", new EmailPayload("newsletter@nexjob.dev", "Weekly digest", ""), "0 9 * * 1");
    return Results.Ok(new { registered = 4 });
});

app.MapPost("/jobs/seed/failed", async (IScheduler scheduler) =>
{
    // Enqueue flakey jobs with failTimes > MaxAttempts so they dead-letter fast
    for (var i = 1; i <= 5; i++)
        await scheduler.EnqueueAsync<FlakeyJob, FlakeyRequest>(
            new FlakeyRequest($"dead-letter-{i}", FailTimes: 99));
    return Results.Accepted(null, new { enqueued = 5, note = "will fail after max attempts" });
});

// ── Chain (continuation) ──────────────────────────────────────────────────────

app.MapPost("/jobs/chain", async (EmailPayload email, IScheduler scheduler) =>
{
    var report = new ReportRequest("sales-summary",
        DateOnly.FromDateTime(DateTime.Today.AddDays(-7)),
        DateOnly.FromDateTime(DateTime.Today));
    var reportId = await scheduler.EnqueueAsync<GenerateReportJob, ReportRequest>(report);
    var emailId  = await scheduler.ContinueWithAsync<SendEmailJob, EmailPayload>(reportId, email);

    return Results.Accepted(null, new
    {
        reportJobId = reportId.Value,
        emailJobId  = emailId.Value,
        description = "Email will be sent after the report finishes",
    });
});

// ── Priority demo ─────────────────────────────────────────────────────────────

app.MapPost("/jobs/email/priority", async (PriorityEmailRequest req, IScheduler scheduler) =>
{
    var id = await scheduler.EnqueueAsync<SendEmailJob, EmailPayload>(req.Email, priority: req.Priority);
    return Results.Accepted($"/jobs/{id.Value}", new { jobId = id.Value, priority = req.Priority.ToString() });
});

// ── Idempotency demo ──────────────────────────────────────────────────────────

app.MapPost("/jobs/email/idempotent", async (IdempotentEmailRequest req, IScheduler scheduler) =>
{
    var id = await scheduler.EnqueueAsync<SendEmailJob, EmailPayload>(req.Email, idempotencyKey: req.IdempotencyKey);
    return Results.Accepted($"/jobs/{id.Value}", new { jobId = id.Value, idempotencyKey = req.IdempotencyKey });
});

// ── Retry stress ──────────────────────────────────────────────────────────────

app.MapPost("/jobs/flakey", async (FlakeyRequest payload, IScheduler scheduler) =>
{
    var id = await scheduler.EnqueueAsync<FlakeyJob, FlakeyRequest>(payload);
    return Results.Accepted($"/jobs/{id.Value}", new { jobId = id.Value });
});

// ── Worker-pool saturation ────────────────────────────────────────────────────

app.MapPost("/jobs/slow", async (SlowRequest payload, IScheduler scheduler) =>
{
    var id = await scheduler.EnqueueAsync<SlowJob, SlowRequest>(payload);
    return Results.Accepted($"/jobs/{id.Value}", new { jobId = id.Value });
});

// ── STRESS: enqueue N jobs of mixed types in one call ────────────────────────

app.MapPost("/jobs/stress", async (StressRequest req, IScheduler scheduler) =>
{
    var ids = new List<object>();
    var rng = new Random();

    for (int i = 0; i < req.Count; i++)
    {
        var priority = (JobPriority)rng.Next(1, 5); // Critical=1 … Low=4

        var jobId = (i % 4) switch
        {
            0 => await scheduler.EnqueueAsync<SendEmailJob, EmailPayload>(
                    new EmailPayload($"user{i}@test.com", $"Subject {i}", "body"),
                    priority: priority),
            1 => await scheduler.EnqueueAsync<GenerateReportJob, ReportRequest>(
                    new ReportRequest($"report-{i}",
                        DateOnly.FromDateTime(DateTime.Today.AddDays(-7)),
                        DateOnly.FromDateTime(DateTime.Today)),
                    priority: priority),
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
    // Fire req.Concurrency requests with the SAME key — only 1 job should be created
    var tasks = Enumerable.Range(0, req.Concurrency)
        .Select(_ => scheduler.EnqueueAsync<SendEmailJob, EmailPayload>(
            new EmailPayload("stress@test.com", "idem-subject", "body"),
            idempotencyKey: req.Key));

    var results = await Task.WhenAll(tasks);
    var distinctIds = results.Select(r => r.Value).Distinct().ToList();

    return Results.Ok(new
    {
        requestsSent  = req.Concurrency,
        distinctJobIds = distinctIds.Count,
        ids           = distinctIds,
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
