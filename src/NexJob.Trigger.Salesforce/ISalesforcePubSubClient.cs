using Eventbus.V1;

namespace NexJob.Trigger.Salesforce;

/// <summary>
/// High-level client contract for interacting with Salesforce Pub/Sub API over gRPC.
/// </summary>
public interface ISalesforcePubSubClient : IDisposable
{
    /// <summary>
    /// Subscribes to a Salesforce topic and yields consumed events as an asynchronous stream.
    /// </summary>
    /// <param name="topic">The Salesforce topic name.</param>
    /// <param name="replayId">The starting binary Replay ID, or null to follow <paramref name="replayPreset"/>.</param>
    /// <param name="replayPreset">The replay preset when no replayId is supplied.</param>
    /// <param name="batchSize">Number of events to request per stream batch.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An asynchronous stream of received <see cref="ConsumerEvent"/> instances.</returns>
    IAsyncEnumerable<ConsumerEvent> SubscribeAsync(
        string topic,
        byte[]? replayId,
        SalesforceReplayPreset replayPreset,
        int batchSize,
        CancellationToken ct);

    /// <summary>
    /// Fetches the Avro schema JSON specification for the given schema ID.
    /// </summary>
    /// <param name="schemaId">The schema identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The raw Avro JSON schema string.</returns>
    Task<string> GetSchemaJsonAsync(string schemaId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves topic metadata information from Salesforce.
    /// </summary>
    /// <param name="topic">The Salesforce topic name.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="TopicInfo"/> returned by the Pub/Sub API.</returns>
    Task<TopicInfo> GetTopicInfoAsync(string topic, CancellationToken ct = default);
}
