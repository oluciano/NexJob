namespace NexJob.Internal;

/// <summary>
/// Describes a registered migration pair used by <see cref="MigrationPipeline"/>
/// to resolve migration chains without open-generic service lookups.
/// </summary>
internal sealed class MigrationDescriptor
{
    /// <summary>Initializes a new <see cref="MigrationDescriptor"/>.</summary>
    public MigrationDescriptor(Type oldType, Type newType)
    {
        OldType = oldType;
        NewType = newType;
    }

    /// <summary>Gets the source (old) input type for this migration step.</summary>
    public Type OldType { get; }

    /// <summary>Gets the target (new) input type for this migration step.</summary>
    public Type NewType { get; }
}
