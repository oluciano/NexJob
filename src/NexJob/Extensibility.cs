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

/// <summary>
/// Migrates a serialized job payload from an older schema version to a newer one.
/// Implement this interface and register it in the DI container to transparently
/// upgrade payloads enqueued before a breaking input change.
/// </summary>
/// <typeparam name="TOld">The previous (source) input type.</typeparam>
/// <typeparam name="TNew">The current (target) input type.</typeparam>
/// <example>
/// <code>
/// public class SendEmailV1ToV2 : IJobMigration&lt;SendEmailInputV1, SendEmailInputV2&gt;
/// {
///     public SendEmailInputV2 Migrate(SendEmailInputV1 old) =>
///         new SendEmailInputV2 { To = old.Recipient, Subject = old.Subject };
/// }
/// </code>
/// </example>
public interface IJobMigration<TOld, TNew>
{
    /// <summary>
    /// Converts an instance of the old payload type into the new payload type.
    /// </summary>
    /// <param name="old">The deserialized payload from the old schema version.</param>
    /// <returns>The migrated payload in the new schema version.</returns>
    TNew Migrate(TOld old);
}
