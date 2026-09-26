namespace NexJob.Exceptions;

/// <summary>
/// Exception thrown when a worker attempts to process a job whose type or input type
/// cannot be loaded or resolved by the current application runtime.
/// </summary>
public sealed class ForeignJobTypeException : System.InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ForeignJobTypeException"/> class.
    /// </summary>
    /// <param name="typeName">The assembly-qualified type name that could not be resolved.</param>
    /// <param name="message">The exception message.</param>
    public ForeignJobTypeException(string typeName, string message)
        : base(message)
    {
        TypeName = typeName;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ForeignJobTypeException"/> class.
    /// </summary>
    /// <param name="typeName">The assembly-qualified type name that could not be resolved.</param>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception that caused this failure.</param>
    public ForeignJobTypeException(string typeName, string message, System.Exception innerException)
        : base(message, innerException)
    {
        TypeName = typeName;
    }

    /// <summary>
    /// Gets the assembly-qualified type name that could not be loaded or resolved.
    /// </summary>
    public string TypeName { get; }
}
