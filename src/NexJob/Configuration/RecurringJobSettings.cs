using NexJob;

namespace NexJob.Configuration;

/// <summary>
/// Configuration for a recurring job definition from appsettings.json.
/// </summary>
public sealed class RecurringJobSettings
{
    /// <summary>
    /// Unique identifier for this recurring job. Required.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Assembly-qualified type name of the job implementation. Required.
    /// Example: "MyApp.Jobs.EmailJob, MyApp".
    /// </summary>
    public string JobType { get; set; } = string.Empty;

    /// <summary>
    /// Assembly-qualified type name of the job's input parameter type.
    /// For jobs without input (IJob), omit this property or set to null.
    /// Example: "MyApp.Jobs.EmailInput, MyApp".
    /// </summary>
    public string? InputType { get; set; }

    /// <summary>
    /// JSON-serialized job input payload. For jobs without input, omit or set to null.
    /// </summary>
    public string? InputJson { get; set; }

    /// <summary>
    /// Cron expression that defines the execution schedule. Required.
    /// Examples:
    /// - "0 0 * * *" - every hour
    /// - "0 9 * * 1-5" - weekdays at 9am
    /// - "*/5 * * * *" - every 5 minutes.
    /// </summary>
    public string Cron { get; set; } = string.Empty;

    /// <summary>
    /// IANA or Windows time-zone ID used when evaluating the cron expression.
    /// Defaults to UTC if null.
    /// Examples: "America/New_York", "Europe/London", "UTC".
    /// </summary>
    public string? TimeZoneId { get; set; }

    /// <summary>
    /// Name of the queue where the recurring job's instances will be enqueued.
    /// Defaults to "default".
    /// </summary>
    public string Queue { get; set; } = "default";

    /// <summary>
    /// Controls what happens when a new firing occurs while a previous instance is still running.
    /// Defaults to SkipIfRunning.
    /// </summary>
    public RecurringConcurrencyPolicy ConcurrencyPolicy { get; set; } = RecurringConcurrencyPolicy.SkipIfRunning;

    /// <summary>
    /// When false, the scheduler skips this job at every firing. Defaults to true.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
