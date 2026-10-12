namespace NexJob.Trigger.Salesforce;

/// <summary>
/// Thrown by an <see cref="ISalesforceSchemaService"/> when an event payload can never be decoded (corrupted Avro, or
/// bytes that do not match the schema). The trigger logs the event, routes it to the dead-letter queue when one is
/// configured and moves on, instead of reconnecting and receiving the same event again. A failure that may pass on a
/// retry, such as a schema that could not be fetched, must not use this type.
/// </summary>
public sealed class SalesforcePayloadDecodeException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="SalesforcePayloadDecodeException"/> class.</summary>
    /// <param name="message">The error message.</param>
    public SalesforcePayloadDecodeException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="SalesforcePayloadDecodeException"/> class.</summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The decoding failure.</param>
    public SalesforcePayloadDecodeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
