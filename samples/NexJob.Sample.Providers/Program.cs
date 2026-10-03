using NexJob;
using NexJob.Dashboard;
using NexJob.Sample.Providers;
using NexJob.Storage;

try
{
    var builder = WebApplication.CreateBuilder(args);

    var provider = ProviderSetup.Register(builder.Services, builder.Configuration);
    builder.Services.AddNexJob(builder.Configuration).AddNexJobJobs(typeof(EchoJob).Assembly);

    var app = builder.Build();

    app.UseNexJobDashboard("/dashboard");

    app.MapGet("/provider", () => Results.Ok(new { provider }));

    app.MapPost("/jobs", async (string? message, IScheduler scheduler) =>
    {
        var id = await scheduler.EnqueueAsync<EchoJob, EchoInput>(new EchoInput(message ?? "hello"));
        return Results.Accepted($"/jobs/{id.Value}", new { jobId = id.Value });
    });

    app.MapGet("/jobs/{id:guid}", async (Guid id, IDashboardStorage storage) =>
    {
        var job = await storage.GetJobByIdAsync(new JobId(id));
        return job is null
            ? Results.NotFound(new { message = $"Job '{id}' not found." })
            : Results.Ok(new { id, status = job.Status.ToString(), job.Attempts });
    });

    await app.RunAsync();
    return 0;
}
catch (InvalidOperationException ex)
{
    await Console.Error.WriteLineAsync($"[Providers sample] {ex.Message}");
    return 1;
}
catch (Exception ex)
{
    await Console.Error.WriteLineAsync($"[Providers sample] Could not start with the configured provider: {ex.GetBaseException().Message}");
    await Console.Error.WriteLineAsync("[Providers sample] Is the database running? Start it with: docker compose up -d postgres | sqlserver | redis | mongodb");
    return 1;
}
