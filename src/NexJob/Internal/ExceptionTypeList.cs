namespace NexJob.Internal;

/// <summary>
/// Validates lists of exception types accepted by the retry configuration.
/// </summary>
internal static class ExceptionTypeList
{
    /// <summary>
    /// Ensures every entry is a non-null type that derives from <see cref="Exception"/>.
    /// </summary>
    /// <param name="types">The list to check; <see langword="null"/> is allowed and returned as is.</param>
    /// <param name="paramName">The name reported when an entry is invalid.</param>
    /// <typeparam name="TList">The list type, returned unchanged.</typeparam>
    /// <returns>The same <paramref name="types"/> when it is valid.</returns>
    internal static TList? Validate<TList>(TList? types, string paramName)
        where TList : class, IEnumerable<Type>
    {
        if (types is null)
        {
            return null;
        }

        foreach (var type in types)
        {
            if (type is null)
            {
                throw new ArgumentException("The list must not contain null entries.", paramName);
            }

            if (!typeof(Exception).IsAssignableFrom(type))
            {
                throw new ArgumentException($"'{type.FullName}' does not derive from System.Exception.", paramName);
            }
        }

        return types;
    }
}
