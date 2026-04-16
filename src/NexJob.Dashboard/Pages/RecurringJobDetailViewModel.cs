using System.Diagnostics.CodeAnalysis;
using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

/// <summary>View model for the Recurring Job Detail page.</summary>
[ExcludeFromCodeCoverage]
internal sealed class RecurringJobDetailViewModel
{
    /// <summary>The recurring job record being displayed.</summary>
    internal required RecurringJobRecord Job { get; init; }

    /// <summary>Paginated list of executions for this recurring job.</summary>
    internal required PagedResult<JobRecord> Executions { get; init; }

    /// <summary>The dashboard path prefix (e.g., "/dashboard").</summary>
    internal required string PathPrefix { get; init; }

    /// <summary>Current UTC time, used for relative time calculations.</summary>
    internal required DateTimeOffset Now { get; init; }

    /// <summary>Short type name (class name only, no namespace).</summary>
    internal string ShortType => Helpers.ShortType(Job.JobType);

    /// <summary>Effective cron expression (override if present, otherwise base cron).</summary>
    internal string EffectiveCron => Job.CronOverride ?? Job.Cron;

    /// <summary>Encoded recurring job ID for use in URLs.</summary>
    internal string EncodedId => Uri.EscapeDataString(Job.RecurringJobId);
}
