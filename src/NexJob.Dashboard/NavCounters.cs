namespace NexJob.Dashboard;

/// <summary>Navigation counters passed into <see cref="HtmlShell.Wrap"/>.
/// All properties are nullable so callers that don't have counter data can use
/// default(T) which is null for reference types — no changes needed to existing callers.</summary>
internal sealed record NavCounters(
    string? Queues,
    string? QueuesClass,
    string? Jobs,
    string? Recurring,
    string? Failed,
    string? FailedClass,
    string? Servers,
    string? ServersClass);
