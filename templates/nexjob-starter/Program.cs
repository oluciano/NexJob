using NexJob;
using NexJob.Dashboard;
using NexJobStarter.Jobs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNexJob(builder.Configuration)
                .AddNexJobJobs(typeof(Program).Assembly);

var app = builder.Build();

app.UseNexJobDashboard("/jobs");

app.MapGet("/", () => Results.Redirect("/jobs"));

app.Lifetime.ApplicationStarted.Register(() =>
{
    var scheduler = app.Services.GetRequiredService<IScheduler>();
    _ = scheduler.EnqueueAsync<WelcomeJob, WelcomeInput>(new WelcomeInput("World"));
});

await app.RunAsync();
