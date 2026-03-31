namespace NexJob.Internal;

/// <summary>
/// Centralizes job type resolution to ensure consistency across runtime execution,
/// dead-letter handling, and other type-dependent operations.
/// </summary>
internal static class JobTypeResolver
{
    /// <summary>
    /// Resolves the CLR <see cref="Type"/> for a job from its persisted type name.
    /// Returns null if the type cannot be loaded (e.g., due to missing assembly or typo).
    /// </summary>
    /// <param name="assemblyQualifiedName">The AssemblyQualifiedName stored in the job record.</param>
    /// <returns>The resolved Type, or null if resolution fails.</returns>
    public static Type? ResolveJobType(string assemblyQualifiedName)
    {
        try
        {
            return Type.GetType(assemblyQualifiedName, throwOnError: false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the CLR <see cref="Type"/> for a job input from its persisted type name.
    /// Returns null if the type cannot be loaded.
    /// </summary>
    /// <param name="assemblyQualifiedName">The AssemblyQualifiedName stored in the job record.</param>
    /// <returns>The resolved Type, or null if resolution fails.</returns>
    public static Type? ResolveInputType(string assemblyQualifiedName)
    {
        try
        {
            return Type.GetType(assemblyQualifiedName, throwOnError: false);
        }
        catch
        {
            return null;
        }
    }
}
