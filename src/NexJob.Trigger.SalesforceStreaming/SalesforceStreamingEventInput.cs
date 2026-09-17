using System.Text.Json;

namespace NexJob.Trigger.SalesforceStreaming;

/// <summary>
/// Represents the payload of an event received from the Salesforce Streaming API (Change Data Capture, Platform Events, or PushTopics).
/// </summary>
/// <param name="ReplayId">The numeric Replay ID assigned by Salesforce.</param>
/// <param name="Channel">The streaming channel on which the event was broadcast.</param>
/// <param name="Payload">The parsed JSON payload of the event data.</param>
/// <param name="CreatedDate">The timestamp when the event was created in Salesforce.</param>
/// <param name="EventId">Optional unique event or transaction identifier.</param>
/// <param name="Schema">Optional schema identifier of the event payload.</param>
public sealed record SalesforceStreamingEventInput(
    long ReplayId,
    string Channel,
    JsonElement Payload,
    DateTimeOffset CreatedDate,
    string? EventId = null,
    string? Schema = null)
{
    /// <summary>
    /// Gets the raw JSON string representation of the event payload.
    /// </summary>
    /// <returns>Raw JSON string.</returns>
    public string GetRawJson() => Payload.ValueKind != JsonValueKind.Undefined
        ? Payload.GetRawText()
        : string.Empty;
}
