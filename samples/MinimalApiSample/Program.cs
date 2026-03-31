using NexJob;
using NexJob.Storage;

var builder = WebApplication.CreateBuilder(args);

// Add NexJob with in-memory storage
builder.Services.AddNexJob(opt => opt.UseInMemory())
               .AddNexJobJobs(typeof(Program).Assembly);

// Register dead-letter handler
builder.Services.AddTransient<IDeadLetterHandler<SendEmailJob>, SendEmailDeadLetterHandler>();

var app = builder.Build();

// Endpoint to enqueue a job
app.MapPost("/send", async (string email, IScheduler scheduler) =>
{
    var jobId = await scheduler.EnqueueAsync<SendEmailJob, SendEmailInput>(
        new(email),
        deadlineAfter: TimeSpan.FromSeconds(10));  // Job expires in 10 seconds if not started

    return Results.Accepted($"/job/{jobId}", new { jobId });
});

// Endpoint to check job status
app.MapGet("/job/{jobId}", async (Guid jobId, IStorageProvider storage) =>
{
    var job = await storage.GetJobByIdAsync(new JobId(jobId));
    if (job is null)
        return Results.NotFound();

    return Results.Ok(new
    {
        job.Id,
        job.Status,
        job.Attempts,
        Deadline = job.ExpiresAt,
    });
});

await app.RunAsync();

// ─── Job Definition ───────────────────────────────────────────────────

public record SendEmailInput(string Email);

public class SendEmailJob : IJob<SendEmailInput>
{
    private readonly ILogger<SendEmailJob> _logger;

    public SendEmailJob(ILogger<SendEmailJob> logger) => _logger = logger;

    public async Task ExecuteAsync(SendEmailInput input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Sending email to {Email}", input.Email);

        // Simulate occasional failure
        if (input.Email.Contains("fail"))
        {
            throw new InvalidOperationException("Simulated failure");
        }

        await Task.Delay(100, cancellationToken);
        _logger.LogInformation("Email sent to {Email}", input.Email);
    }
}

// ─── Dead-Letter Handler ──────────────────────────────────────────────

public class SendEmailDeadLetterHandler : IDeadLetterHandler<SendEmailJob>
{
    private readonly ILogger<SendEmailDeadLetterHandler> _logger;

    public SendEmailDeadLetterHandler(ILogger<SendEmailDeadLetterHandler> logger)
        => _logger = logger;

    public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
    {
        _logger.LogError(
            "📧 Email job failed permanently after {Attempts} attempts. Error: {Error}",
            failedJob.Attempts,
            lastException.Message);

        // In production: send alert, record incident, trigger compensation, etc.
        return Task.CompletedTask;
    }
}
