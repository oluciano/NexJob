namespace NexJob.Trigger.SalesforceStreaming;

/// <summary>
/// Exception thrown when a Bayeux/CometD protocol error occurs with Salesforce.
/// </summary>
public sealed class SalesforceBayeuxException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SalesforceBayeuxException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">Optional Bayeux error code (e.g. "403::Unknown client").</param>
    /// <param name="shouldRehandshake">Indicates whether the client session is expired and requires a new handshake.</param>
    public SalesforceBayeuxException(string message, string? errorCode = null, bool shouldRehandshake = false)
        : base(message)
    {
        ErrorCode = errorCode;
        ShouldRehandshake = shouldRehandshake;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SalesforceBayeuxException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    /// <param name="errorCode">Optional Bayeux error code.</param>
    /// <param name="shouldRehandshake">Indicates whether the client session requires a new handshake.</param>
    public SalesforceBayeuxException(string message, Exception innerException, string? errorCode = null, bool shouldRehandshake = false)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        ShouldRehandshake = shouldRehandshake;
    }

    /// <summary>
    /// Gets the Bayeux protocol error code if present in the response.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Gets a value indicating whether the current Bayeux client session is dead and a fresh handshake is required.
    /// </summary>
    public bool ShouldRehandshake { get; }
}
