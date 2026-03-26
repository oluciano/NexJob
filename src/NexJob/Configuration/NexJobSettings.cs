namespace NexJob.Configuration;

/// <summary>
/// Strongly-typed representation of the <c>NexJob</c> section in <c>appsettings.json</c>.
/// Bind via <c>configuration.GetSection(NexJobSettings.SectionName).Get&lt;NexJobSettings&gt;()</c>
/// or pass an <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> directly to
/// <see cref="NexJobServiceCollectionExtensions.AddNexJob(Microsoft.Extensions.DependencyInjection.IServiceCollection,Microsoft.Extensions.Configuration.IConfiguration)"/>.
/// </summary>
public sealed class NexJobSettings
{
    /// <summary>The configuration section name: <c>NexJob</c>.</summary>
    public const string SectionName = "NexJob";

    /// <summary>Maximum number of concurrent workers. Defaults to <c>10</c>.</summary>
    public int Workers { get; set; } = 10;

    /// <summary>Maximum number of execution attempts before dead-lettering. Defaults to <c>10</c>.</summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>Default queue name. Defaults to <c>default</c>.</summary>
    public string DefaultQueue { get; set; } = "default";

    /// <summary>How often the dispatcher polls for new jobs. Defaults to <c>15 seconds</c>.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How often workers refresh their heartbeat. Defaults to <c>30 seconds</c>.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Time after which a Processing job with a stale heartbeat is re-enqueued. Defaults to <c>5 minutes</c>.</summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Maximum number of log lines captured per job execution. Defaults to <c>200</c>.</summary>
    public int MaxJobLogLines { get; set; } = 200;

    /// <summary>Per-queue configuration, including optional execution windows.</summary>
    public List<QueueSettings> Queues { get; set; } = [];

    /// <summary>Dashboard-specific settings.</summary>
    public DashboardSettings Dashboard { get; set; } = new();
}
