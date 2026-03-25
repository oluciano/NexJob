namespace NexJob;

/// <summary>
/// Priority levels for job execution. Lower numeric values are processed first.
/// </summary>
public enum JobPriority
{
    /// <summary>Processed before all other levels. Reserved for time-critical operations.</summary>
    Critical = 1,

    /// <summary>Processed after <see cref="Critical"/> jobs.</summary>
    High = 2,

    /// <summary>Default priority for most background work.</summary>
    Normal = 3,

    /// <summary>Processed only when no higher-priority jobs are waiting.</summary>
    Low = 4,
}
