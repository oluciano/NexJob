using System.Globalization;

namespace NexJob;

/// <summary>
/// Sets the maximum time a job type may run, overriding <see cref="NexJobOptions.DefaultExecutionTimeout"/>.
/// </summary>
/// <remarks>
/// <para>
/// When the limit is reached the token passed to <c>ExecuteAsync</c> is cancelled and the run is recorded as a
/// failure (<see cref="TimeoutException"/>). It then follows the normal failure path: it consumes the attempt,
/// is retried, and is dead-lettered on the last attempt.
/// </para>
/// <para>
/// Cancellation is cooperative. A job that ignores its <see cref="CancellationToken"/> keeps its worker slot
/// until it returns. The timer starts once the job holds its <see cref="ThrottleAttribute"/> slots, so waiting
/// for a throttled resource never counts towards the limit.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [ExecutionTimeout("00:05:00")]
/// public class ReportJob : IJob&lt;ReportInput&gt; { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class ExecutionTimeoutAttribute : Attribute
{
    /// <summary>
    /// Initializes a new <see cref="ExecutionTimeoutAttribute"/>.
    /// </summary>
    /// <param name="timeout">The limit as a <see cref="TimeSpan"/> string (for example <c>"00:05:00"</c>). Must be greater than zero.</param>
    public ExecutionTimeoutAttribute(string timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeout);

        if (!TimeSpan.TryParse(timeout, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new ArgumentException($"'{timeout}' is not a valid TimeSpan.", nameof(timeout));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(parsed, TimeSpan.Zero);
        Timeout = parsed;
    }

    /// <summary>Gets the maximum execution time.</summary>
    public TimeSpan Timeout { get; }
}
