namespace NexJob;

/// <summary>
/// Customizes the retention and storage optimization behavior for a specific job type.
/// Apply this attribute to an <see cref="IJob{TInput}"/> or <see cref="IJob"/> implementation
/// to enable aggressive anti-bloat strategies for high-throughput or streaming workloads.
/// </summary>
/// <example>
/// <code>
/// // Ephemeral job: purged immediately upon successful completion
/// [Retention(PurgeOnSuccess = true)]
/// public sealed class IngestTelemetryJob : IJob&lt;TelemetryInput&gt; { ... }
///
/// // Audit-friendly job: keeps status and metadata, but clears heavy payload
/// [Retention(TrimPayloadOnSuccess = true)]
/// public sealed class ProcessHeavyPayloadJob : IJob&lt;LargePayload&gt; { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RetentionAttribute : Attribute
{
    /// <summary>
    /// Gets or sets a value indicating whether the job record should be permanently deleted
    /// immediately upon successful completion, generating zero database bloat.
    /// If the job fails, it is retained normally for investigation and dead-lettering.
    /// </summary>
    public bool PurgeOnSuccess { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the job's serialized input payload (<c>input_json</c>)
    /// should be stripped/cleared upon successful completion, reducing row footprint while
    /// preserving execution history and audit timestamps.
    /// </summary>
    public bool TrimPayloadOnSuccess { get; init; }
}
