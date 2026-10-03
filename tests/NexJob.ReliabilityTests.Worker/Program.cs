using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexJob;
using NexJob.ReliabilityTests;
using NexJob.SqlServer;

// A NexJob node in its own process. Arguments: <SQL Server connection string> <queue> <marker file>.
// The job it runs writes the marker file as soon as it starts and then never finishes, so the test can kill the process
// while the job is running.
if (args.Length != 3)
{
    await Console.Error.WriteLineAsync("Usage: <connection string> <queue> <marker file>");
    return 2;
}

var connectionString = args[0];
var queue = args[1];
var markerPath = args[2];

using var host = Host.CreateDefaultBuilder()
    .ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
    .ConfigureServices(services =>
    {
        services.AddNexJobSqlServer(connectionString);
        services.AddNexJob(options =>
        {
            options.Workers = 1;
            options.Queues = [queue];
            options.PollingInterval = TimeSpan.FromMilliseconds(100);
            options.HeartbeatInterval = TimeSpan.FromMilliseconds(300);
            options.MaxAttempts = 3;
        });
        services.AddSingleton(new CrashMarker(markerPath));
        services.AddSingleton<ExecutionLog>();
        services.AddTransient<CrashTargetJob>();
    })
    .Build();

await host.RunAsync();
return 0;
