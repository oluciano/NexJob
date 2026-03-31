using System.Diagnostics;
using System.Reflection;

namespace NexJob.Telemetry;

/// <summary>
/// Provides the shared <see cref="ActivitySource"/> and W3C TraceContext propagation
/// helpers used throughout NexJob's instrumentation.
/// </summary>
public static class NexJobActivitySource
{
    /// <summary>The name of the NexJob activity source, for use with OpenTelemetry SDK configuration.</summary>
    public const string Name = "NexJob";

    internal static readonly ActivitySource Source =
        new(Name, Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0");

    /// <summary>
    /// Starts a new <see cref="Activity"/> for a job enqueue operation.
    /// Returns <see langword="null"/> when no listener is subscribed.
    /// </summary>
    /// <param name="jobType">The fully-qualified job type name.</param>
    /// <param name="queue">The target queue name.</param>
    internal static Activity? StartEnqueue(string jobType, string queue) =>
        Source.StartActivity(
            "nexjob.enqueue",
            ActivityKind.Producer,
            parentContext: default,
            tags:
            [
                new("nexjob.job_type", jobType),
                new("nexjob.queue", queue),
            ]);

    /// <summary>
    /// Starts a new <see cref="Activity"/> for a job execution span, optionally
    /// restoring a parent trace context propagated from the enqueue call.
    /// </summary>
    /// <param name="jobType">The fully-qualified job type name.</param>
    /// <param name="queue">The queue the job was fetched from.</param>
    /// <param name="traceParent">W3C traceparent header value stored at enqueue time, or <see langword="null"/>.</param>
    internal static Activity? StartExecute(string jobType, string queue, string? traceParent)
    {
        ActivityContext parentContext = default;

        if (traceParent is not null)
        {
            ActivityContext.TryParse(traceParent, null, out parentContext);
        }

        return Source.StartActivity(
            "nexjob.execute",
            ActivityKind.Consumer,
            parentContext,
            tags:
            [
                new("nexjob.job_type", jobType),
                new("nexjob.queue", queue),
            ]);
    }

    /// <summary>
    /// Starts a new <see cref="Activity"/> for a recurring job registration operation.
    /// Returns <see langword="null"/> when no listener is subscribed.
    /// </summary>
    /// <param name="jobType">The fully-qualified job type name.</param>
    /// <param name="recurringJobId">The unique identifier for the recurring job definition.</param>
    internal static Activity? StartRecurring(string jobType, string recurringJobId) =>
        Source.StartActivity(
            "nexjob.recurring.register",
            ActivityKind.Internal,
            parentContext: default,
            tags:
            [
                new("nexjob.job_type", jobType),
                new("nexjob.recurring_job_id", recurringJobId),
            ]);
}
