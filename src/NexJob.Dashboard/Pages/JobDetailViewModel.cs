using NexJob.Storage;

namespace NexJob.Dashboard.Pages;

/// <summary>View model for the Job Detail page.</summary>
internal sealed class JobDetailViewModel
{
    /// <summary>The job record being displayed.</summary>
    internal required JobRecord Job { get; init; }

    /// <summary>The dashboard path prefix (e.g., "/dashboard").</summary>
    internal required string PathPrefix { get; init; }

    /// <summary>Current UTC time, used for relative time calculations.</summary>
    internal required DateTimeOffset Now { get; init; }

    /// <summary>Short type name (class name only, no namespace).</summary>
    internal string ShortType => Helpers.ShortType(Job.JobType);

    /// <summary>Formatted job ID (first 8 chars with ellipsis).</summary>
    internal string FormattedId => Job.Id.Value.ToString()[..8] + "…";
}
