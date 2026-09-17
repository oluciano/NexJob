namespace NexJob.Trigger.Salesforce;

/// <summary>
/// Structured payload delivered to NexJob jobs representing a consumed Salesforce event.
/// </summary>
public sealed class SalesforceEventPayload
{
    /// <summary>
    /// Gets or sets the Salesforce topic name.
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the hex representation of the binary Replay ID.
    /// </summary>
    public string ReplayIdHex { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the schema fingerprint ID.
    /// </summary>
    public string SchemaId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the decoded JSON representation of the Avro payload.
    /// </summary>
    public string PayloadJson { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets headers extracted from the Salesforce event.
    /// </summary>
    public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
