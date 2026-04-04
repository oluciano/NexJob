namespace NexJob;

/// <summary>
/// Strongly-typed identifier for a job. Wraps a <see cref="Guid"/> to prevent accidental
/// mixing with other identifiers.
/// </summary>
public readonly record struct JobId(Guid Value)
{
    /// <summary>Creates a new <see cref="JobId"/> with a fresh random GUID.</summary>
    public static JobId New() => new(Guid.NewGuid());

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
// Final AI review test
