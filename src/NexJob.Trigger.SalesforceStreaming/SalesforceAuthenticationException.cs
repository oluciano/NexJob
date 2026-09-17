namespace NexJob.Trigger.SalesforceStreaming;

/// <summary>
/// Exception thrown when authentication with Salesforce fails.
/// </summary>
public sealed class SalesforceAuthenticationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SalesforceAuthenticationException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    public SalesforceAuthenticationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SalesforceAuthenticationException"/> class.
    /// </summary>
    /// <param name="message">The exception message.</param>
    /// <param name="innerException">The inner exception.</param>
    public SalesforceAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
