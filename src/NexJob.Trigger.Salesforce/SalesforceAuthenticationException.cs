namespace NexJob.Trigger.Salesforce;

/// <summary>
/// Exception thrown when Salesforce OAuth2 authentication fails.
/// </summary>
public sealed class SalesforceAuthenticationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SalesforceAuthenticationException"/> class.
    /// </summary>
    /// <param name="message">Diagnostic failure message.</param>
    public SalesforceAuthenticationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SalesforceAuthenticationException"/> class with an inner exception.
    /// </summary>
    /// <param name="message">Diagnostic failure message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public SalesforceAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
