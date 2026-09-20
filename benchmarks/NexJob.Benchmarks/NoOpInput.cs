namespace NexJob.Benchmarks;

/// <summary>Input for <see cref="NoOpJob"/>.</summary>
public sealed record NoOpInput
{
    /// <summary>Gets or sets the arbitrary payload used to benchmark serialization overhead.</summary>
    public string Payload { get; init; } = string.Empty;
}
