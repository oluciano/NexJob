using System.Globalization;

namespace NexJob;

/// <summary>
/// Configures the retry policy for a specific job type, overriding the global defaults
/// set on <see cref="NexJobOptions"/>.
/// </summary>
/// <example>
/// <code>
/// [Retry(5, InitialDelay = "00:00:30", Multiplier = 2.0, MaxDelay = "01:00:00")]
/// public class PaymentJob : IJob&lt;PaymentInput&gt; { ... }
///
/// [Retry(0)]  // dead-letter immediately on failure, no retries
/// public class WebhookJob : IJob&lt;WebhookInput&gt; { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RetryAttribute : Attribute
{
    private readonly Type[]? _ignoreRetryAttemptExceptions;

    /// <summary>
    /// Initializes a new <see cref="RetryAttribute"/>.
    /// </summary>
    /// <param name="attempts">Maximum number of retry attempts. Must be non-negative.</param>
    public RetryAttribute(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attempts);
        Attempts = attempts;
    }

    /// <summary>
    /// Maximum number of executions, including the first run (so <c>Attempts - 1</c> retries).
    /// <c>0</c> and <c>1</c> both mean no retry: the job is dead-lettered on its first failure.
    /// </summary>
    public int Attempts { get; }

    /// <summary>
    /// Initial delay before the first retry, as a <see cref="TimeSpan"/> string
    /// (e.g. <c>"00:00:30"</c> for 30 seconds).
    /// When <see langword="null"/>, the global <see cref="NexJobOptions.RetryDelayFactory"/> is used.
    /// </summary>
    public string? InitialDelay { get; init; }

    /// <summary>
    /// Multiplier applied to the delay on each subsequent retry.
    /// For example, <c>2.0</c> doubles the delay on each attempt. Default: <c>2.0</c>.
    /// </summary>
    public double Multiplier { get; init; } = 2.0;

    /// <summary>
    /// Maximum delay cap as a <see cref="TimeSpan"/> string (e.g. <c>"01:00:00"</c> for 1 hour).
    /// When <see langword="null"/>, no cap is applied.
    /// </summary>
    public string? MaxDelay { get; init; }

    /// <summary>
    /// Exception types that must not be retried for this job type. When a run fails with one of them (or a type
    /// derived from it), the job goes straight to <c>Failed</c> and the dead-letter handler runs, without waiting
    /// for the remaining attempts. The failure is still a failure: this does not make the job succeed.
    /// Combined with <see cref="NexJobOptions.IgnoreRetryAttemptExceptions"/>. Every type must derive from
    /// <see cref="Exception"/>.
    /// </summary>
    public Type[]? IgnoreRetryAttemptExceptions
    {
        get => _ignoreRetryAttemptExceptions;
        init => _ignoreRetryAttemptExceptions = Internal.ExceptionTypeList.Validate(value, nameof(value));
    }

    /// <summary>
    /// Computes the retry delay for a given attempt number using this attribute's configuration.
    /// </summary>
    /// <param name="attempt">1-based attempt number.</param>
    /// <returns>The delay before the next retry attempt.</returns>
    internal TimeSpan ComputeDelay(int attempt)
    {
        if (InitialDelay is null)
        {
            return TimeSpan.Zero; // caller falls back to global factory
        }

        var initial = TimeSpan.Parse(InitialDelay, CultureInfo.InvariantCulture);
        var delay = TimeSpan.FromTicks((long)(initial.Ticks * Math.Pow(Multiplier, attempt - 1)));

        if (MaxDelay is not null)
        {
            var max = TimeSpan.Parse(MaxDelay, CultureInfo.InvariantCulture);
            if (delay > max)
            {
                delay = max;
            }
        }

        // Add ±10% jitter
        var jitterFactor = 1.0 + (0.1 * ((((double)System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue) / int.MaxValue) * 2.0) - 1.0));
        return TimeSpan.FromTicks((long)(delay.Ticks * jitterFactor));
    }
}
