using System.Text.Json;

namespace NexJob.Configuration;

/// <summary>
/// Configuration for a recurring job definition from appsettings.json.
/// Job resolution uses simple class names (no assembly qualification required) registered via
/// <see cref="NexJobServiceCollectionExtensions.AddNexJobJobs"/>. Input type is inferred
/// from the job's <see cref="IJob{TInput}"/> interface — no explicit declaration needed.
/// </summary>
public sealed class RecurringJobSettings
{
    /// <summary>
    /// Simple class name of the job implementation (e.g. "CleanupJob").
    /// Resolved from types registered via <see cref="NexJobServiceCollectionExtensions.AddNexJobJobs"/>.
    /// Required.
    /// </summary>
    public string Job { get; set; } = string.Empty;

    /// <summary>
    /// Unique identifier for this recurring job. Optional.
    /// If omitted, the framework derives it from the job name: "{JobName}" if unique,
    /// or "{JobName}-{index}" if the same job appears multiple times in configuration.
    /// Use explicit Id when scheduling the same job multiple times with different inputs/schedules.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// JSON string representing the job input payload. Optional.
    /// Omit for <see cref="IJob"/> (no-input) jobs.
    /// Example: "{\"Region\": \"us-east\", \"Limit\": 100}"
    /// Input type is automatically inferred from the job's <see cref="IJob{TInput}"/> interface.
    /// </summary>
    public string? Input { get; set; }

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

    /// <summary>
    /// Internal: Pre-resolved job type (set by fluent API, bypasses DI registry lookup).
    /// </summary>
    internal Type? ResolvedJobType { get; set; }

    /// <summary>
    /// Internal: Pre-serialized input JSON (set by fluent API).
    /// </summary>
    internal string? ResolvedInputJson { get; set; }

    /// <summary>
    /// Internal: Effective identifier — explicit Id or derived from Job name.
    /// Actual ID assignment (handling duplicates with -{index}) happens during registration.
    /// </summary>
    internal string EffectiveId => string.IsNullOrWhiteSpace(Id) ? Job : Id;
}
