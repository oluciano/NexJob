using Google.Cloud.PubSub.V1;

namespace NexJob.Trigger.GooglePubSub;

/// <summary>
/// Abstraction over the Pub/Sub subscriber for testability.
/// </summary>
internal interface IPubSubSubscriber : IAsyncDisposable
{
    /// <summary>
    /// Starts the subscriber. The handler is called for each received message.
    /// Return Reply.Ack to acknowledge, Reply.Nack to nack.
    /// </summary>
    /// <param name="handler">The message handler.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the subscriber operation.</returns>
    Task StartAsync(Func<PubsubMessage, CancellationToken, Task<SubscriberClient.Reply>> handler, CancellationToken ct);

    /// <summary>
    /// Stops the subscriber gracefully.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the stop operation.</returns>
    Task StopAsync(CancellationToken ct);
}
