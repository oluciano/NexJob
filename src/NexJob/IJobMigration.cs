namespace NexJob;

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
public interface IJobMigration<in TOld, out TNew>
{
    /// <summary>
    /// Converts an instance of the old payload type into the new payload type.
    /// </summary>
    /// <param name="old">The deserialized payload from the old schema version.</param>
    /// <returns>The migrated payload in the new schema version.</returns>
    TNew Migrate(TOld old);
}
