using Google.Cloud.PubSub.V1;

namespace NexJob.Trigger.GooglePubSub;

/// <summary>
/// Wrapper for Google Cloud Pub/Sub SubscriberClient implementing <see cref="IPubSubSubscriber"/>.
/// </summary>
internal sealed class GooglePubSubSubscriber : IPubSubSubscriber
{
    private readonly SubscriberClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="GooglePubSubSubscriber"/> class.
    /// </summary>
    /// <param name="client">The subscriber client.</param>
    public GooglePubSubSubscriber(SubscriberClient client) => _client = client;

    /// <inheritdoc/>
    public Task StartAsync(Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>> handler, CancellationToken ct) =>
        _client.StartAsync(handler);

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken ct) => _client.StopAsync(ct);

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() => await _client.DisposeAsync().ConfigureAwait(false);
}
