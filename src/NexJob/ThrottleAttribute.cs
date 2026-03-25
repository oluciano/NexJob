namespace NexJob;

/// <summary>
/// Limits the number of concurrent executions of a job type across all workers.
/// Apply this attribute to an <see cref="IJob{TInput}"/> implementation to prevent
/// resource exhaustion on shared external systems.
/// </summary>
/// <remarks>
/// Multiple <see cref="ThrottleAttribute"/> instances may be applied to the same job
/// to throttle against several independent resources simultaneously.
/// </remarks>
/// <example>
/// <code>
/// [Throttle("payment-gateway", maxConcurrent: 5)]
/// public class ChargeCardJob : IJob&lt;ChargeCardInput&gt; { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class ThrottleAttribute : Attribute
{
    /// <summary>
    /// Initializes a new <see cref="ThrottleAttribute"/>.
    /// </summary>
    /// <param name="resource">
    /// A logical resource name shared across job types that contend for the same limit.
    /// </param>
    /// <param name="maxConcurrent">
    /// Maximum number of jobs allowed to execute concurrently against <paramref name="resource"/>.
    /// </param>
    public ThrottleAttribute(string resource, int maxConcurrent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrent, 1);

        Resource = resource;
        MaxConcurrent = maxConcurrent;
    }

    /// <summary>Logical resource name used to group throttled jobs.</summary>
    public string Resource { get; }

    /// <summary>Maximum number of concurrent executions allowed for <see cref="Resource"/>.</summary>
    public int MaxConcurrent { get; }
}
