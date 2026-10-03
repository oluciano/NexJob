using NexJob;
using NexJob.Dashboard;
using NexJob.Sample.Reliability;
using NexJob.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<SampleLog>();
builder.Services.AddTransient<IDeadLetterHandler<DeadLetterDemoJob>, DeadLetterDemoHandler>();

// Queues come from appsettings.json. The circuit breaker is configured in code: five failures in a row would be the default,
// three makes the demo short.
builder.Services.AddNexJob(builder.Configuration, opt =>
{
    opt.ConfigureQueue("fragile", queue => queue.EnableCircuitBreaker(circuit =>
    {
        circuit.ConsecutiveFailuresThreshold = 3;
        circuit.OpenDuration = TimeSpan.FromMinutes(5);
    }));
}).AddNexJobJobs(typeof(FlakyJob).Assembly);

builder.Services.AddHealthChecks().AddNexJob();

var app = builder.Build();

app.UseNexJobDashboard("/dashboard");
app.MapHealthChecks("/health");

// ── Retry: [Retry(4, InitialDelay = "00:00:01")] on FlakyJob. Fails twice, succeeds on the third attempt. ──
app.MapPost("/retry/flaky", async (IScheduler scheduler) =>
    Results.Accepted(null, new { jobId = (await scheduler.EnqueueAsync<FlakyJob>()).Value }));

// ── Checkpoint: crashes after step 3 on the first attempt, the retry resumes at step 4. ──
app.MapPost("/checkpoint", async (IScheduler scheduler) =>
    Results.Accepted(null, new { jobId = (await scheduler.EnqueueAsync<CheckpointJob>()).Value }));

// ── Dead-letter: two attempts fail, then DeadLetterDemoHandler runs (see GET /log). ──
app.MapPost("/deadletter", async (IScheduler scheduler) =>
    Results.Accepted(null, new { jobId = (await scheduler.EnqueueAsync<DeadLetterDemoJob>()).Value }));

// ── Deadline: the "held" queue is paused, so the job waits longer than its 2 second deadline and expires when the queue
//    is resumed (POST /queues/held/resume). ──
app.MapPost("/deadline/demo", async (IScheduler scheduler, IJobControlService control, NexJobOptions options) =>
{
    await control.PauseQueueAsync("held");
    await Task.Delay(options.PollingInterval * 3); // pausing takes effect on the next polling cycle
    var id = await scheduler.EnqueueAsync<QuickJob>(queue: "held", deadlineAfter: TimeSpan.FromSeconds(2));
    return Results.Accepted(null, new { jobId = id.Value, next = "wait 3 seconds, then POST /queues/held/resume and GET /jobs/{jobId}" });
});

// ── Circuit breaker: three failures in a row on "fragile" open the circuit; a probe job then waits. ──
app.MapPost("/circuit/trip", async (IScheduler scheduler) =>
{
    var ids = new List<Guid>();
    for (var i = 0; i < 3; i++)
    {
        ids.Add((await scheduler.EnqueueAsync<AlwaysFailJob>(queue: "fragile")).Value);
    }

    return Results.Accepted(null, new { jobIds = ids, next = "POST /circuit/probe, then POST /queues/fragile/reset-circuit" });
});

app.MapPost("/circuit/probe", async (IScheduler scheduler) =>
    Results.Accepted(null, new { jobId = (await scheduler.EnqueueAsync<QuickJob>(queue: "fragile")).Value }));

// ── Programmatic control: IJobControlService ──
app.MapPost("/queues/{queue}/pause", async (string queue, IJobControlService control) =>
{
    await control.PauseQueueAsync(queue);
    return Results.Ok(new { queue, paused = true });
});

app.MapPost("/queues/{queue}/resume", async (string queue, IJobControlService control) =>
{
    await control.ResumeQueueAsync(queue);
    return Results.Ok(new { queue, paused = false });
});

app.MapPost("/queues/{queue}/reset-circuit", async (string queue, IJobControlService control) =>
{
    await control.ResetQueueCircuitAsync(queue);
    return Results.Ok(new { queue, circuit = "Closed" });
});

app.MapPost("/jobs/{id:guid}/requeue", async (Guid id, IJobControlService control) =>
{
    await control.RequeueJobAsync(new JobId(id));
    return Results.Accepted(null, new { jobId = id });
});

app.MapDelete("/jobs/{id:guid}", async (Guid id, IJobControlService control) =>
{
    await control.DeleteJobAsync(new JobId(id));
    return Results.NoContent();
});

app.MapGet("/jobs/{id:guid}", async (Guid id, IDashboardStorage storage) =>
{
    var job = await storage.GetJobByIdAsync(new JobId(id));
    return job is null
        ? Results.NotFound(new { message = $"Job '{id}' not found." })
        : Results.Ok(new { id, status = job.Status.ToString(), job.Attempts, job.Queue, error = job.LastErrorMessage });
});

app.MapGet("/log", (SampleLog log) => Results.Ok(log.Snapshot()));

await app.RunAsync();
