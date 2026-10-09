namespace NexJob.DocsTruth;

/// <summary>How a finding is treated by <c>--strict</c>.</summary>
internal enum Severity
{
    /// <summary>The wiki and the code disagree; fails <c>--strict</c>.</summary>
    Drift,

    /// <summary>Something a person should look at; never fails <c>--strict</c>.</summary>
    Review,
}
