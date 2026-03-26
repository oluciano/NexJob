namespace NexJob;

/// <summary>
/// Declares the current schema version of a job's input payload.
/// Apply this attribute to an <see cref="IJob{TInput}"/> implementation to participate
/// in the automatic migration pipeline. When absent, version 1 is assumed.
/// </summary>
/// <example>
/// <code>
/// [SchemaVersion(2)]
/// public class SendEmailJob : IJob&lt;SendEmailInputV2&gt; { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class SchemaVersionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new <see cref="SchemaVersionAttribute"/>.
    /// </summary>
    /// <param name="version">The current schema version. Must be greater than or equal to 1.</param>
    public SchemaVersionAttribute(int version)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        Version = version;
    }

    /// <summary>Gets the declared schema version.</summary>
    public int Version { get; }
}
