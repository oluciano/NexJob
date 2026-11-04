namespace NexJob.Trigger.Salesforce;

/// <summary>
/// Thrown inside the trigger when an event could not be enqueued and could not be routed to the dead-letter queue.
/// The stream loop catches it to reconnect from the last committed Replay ID, and to skip the event once it has failed
/// <see cref="SalesforceTriggerOptions.MaxEnqueueAttempts"/> times in a row.
/// </summary>
public sealed class SalesforceEnqueueFailedException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="SalesforceEnqueueFailedException"/> class.</summary>
    /// <param name="idempotencyKey">The idempotency key of the event that failed.</param>
    /// <param name="replayId">The Replay ID of the event that failed.</param>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The enqueue failure.</param>
    public SalesforceEnqueueFailedException(string idempotencyKey, byte[] replayId, string message, Exception innerException)
        : base(message, innerException)
    {
        IdempotencyKey = idempotencyKey;
        ReplayId = replayId;
    }

    /// <summary>Gets the idempotency key of the event that failed.</summary>
    public string IdempotencyKey { get; }

    /// <summary>Gets the Replay ID of the event that failed.</summary>
    public byte[] ReplayId { get; }
}
