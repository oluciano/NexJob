namespace NexJob.Internal;

/// <summary>
/// Centralizes job type resolution to ensure consistency across runtime execution,
/// dead-letter handling, and other type-dependent operations.
/// </summary>
internal static class JobTypeResolver
{
    /// <summary>
    /// Resolves the CLR <see cref="Type"/> for a job from its assembly-qualified name.
    /// Returns null if the job type cannot be loaded.
    /// </summary>
    /// <param name="assemblyQualifiedName">The AssemblyQualifiedName stored in the job record.</param>
    /// <returns>The resolved job <see cref="Type"/>, or null if resolution fails.</returns>
    public static Type? ResolveJobType(string assemblyQualifiedName) =>
        Type.GetType(assemblyQualifiedName, throwOnError: false);

    /// <summary>
    /// Resolves the CLR input <see cref="Type"/> for a job from its assembly-qualified name.
    /// Returns null if the input type cannot be loaded.
    /// </summary>
    /// <param name="assemblyQualifiedName">The AssemblyQualifiedName of the input stored in the job record.</param>
    /// <returns>The resolved input <see cref="Type"/>, or null if resolution fails.</returns>
    public static Type? ResolveInputType(string assemblyQualifiedName) =>
        Type.GetType(assemblyQualifiedName, throwOnError: false);
}
