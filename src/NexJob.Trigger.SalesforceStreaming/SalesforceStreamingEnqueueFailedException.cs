namespace NexJob.Trigger.SalesforceStreaming;

/// <summary>
/// Thrown inside the trigger when an event could not be enqueued and could not be routed to the dead-letter queue.
/// The subscription loop catches it to reconnect from the last committed Replay ID, and to skip the event once it has
/// failed <see cref="SalesforceStreamingTriggerOptions.MaxEnqueueAttempts"/> times in a row.
/// </summary>
public sealed class SalesforceStreamingEnqueueFailedException : InvalidOperationException
{
    /// <summary>Initializes a new instance of the <see cref="SalesforceStreamingEnqueueFailedException"/> class.</summary>
    /// <param name="idempotencyKey">The idempotency key of the event that failed.</param>
    /// <param name="replayId">The Replay ID of the event that failed.</param>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The enqueue failure.</param>
    public SalesforceStreamingEnqueueFailedException(string idempotencyKey, long replayId, string message, Exception innerException)
        : base(message, innerException)
    {
        IdempotencyKey = idempotencyKey;
        ReplayId = replayId;
    }

    /// <summary>Gets the idempotency key of the event that failed.</summary>
    public string IdempotencyKey { get; }

    /// <summary>Gets the Replay ID of the event that failed.</summary>
    public long ReplayId { get; }
}
