using System.Reflection;
using NexJob;
using NexJob.Dashboard;
using TesteNextJob.Jobs;

var builder = WebApplication.CreateBuilder(args);

// ── NexJob Configuration ──────────────────────────────────────────────────────
builder.Services.AddNexJob(builder.Configuration, opt =>
{
    opt.PollingInterval = TimeSpan.FromSeconds(1);
    opt.MaxAttempts = 3;
});

// Auto-register all IJob and IJob<T> implementations
builder.Services.AddNexJobJobs(typeof(TesteOnlyJob).Assembly);

var app = builder.Build();

// ── Dashboard ─────────────────────────────────────────────────────────────────
app.UseNexJobDashboard("/dashboard");

// ── Health check endpoint ─────────────────────────────────────────────────────
app.MapGet("/", () => Results.Ok(new { message = "TesteNextJob is running", dashboardUrl = "/dashboard" }));

await app.RunAsync();
