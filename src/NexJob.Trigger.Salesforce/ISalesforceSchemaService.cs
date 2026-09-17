namespace NexJob.Trigger.Salesforce;

/// <summary>
/// Service contract for resolving, caching, and decoding Salesforce Avro schemas and binary payloads.
/// </summary>
public interface ISalesforceSchemaService
{
    /// <summary>
    /// Decodes an Avro binary payload using the specified schema ID into a JSON string.
    /// </summary>
    /// <param name="schemaId">The schema fingerprint identifier.</param>
    /// <param name="payloadBytes">The binary Avro payload bytes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Decoded JSON payload string.</returns>
    ValueTask<string> DecodePayloadToJsonAsync(string schemaId, byte[] payloadBytes, CancellationToken ct = default);

    /// <summary>
    /// Registers or pre-warms a schema definition in the local cache.
    /// </summary>
    /// <param name="schemaId">The schema fingerprint identifier.</param>
    /// <param name="schemaJson">The Avro schema JSON specification.</param>
    void RegisterSchema(string schemaId, string schemaJson);
}
