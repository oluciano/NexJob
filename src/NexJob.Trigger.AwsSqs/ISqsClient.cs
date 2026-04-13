using Amazon.SQS.Model;

namespace NexJob.Trigger.AwsSqs;

/// <summary>
/// Abstraction over the SQS client methods used by <see cref="AwsSqsTrigger"/>.
/// Enables testability without depending on the full <c>IAmazonSQS</c> interface.
/// </summary>
public interface ISqsClient
{
    /// <summary>
    /// Receives messages from an SQS queue.
    /// </summary>
    Task<ReceiveMessageResponse> ReceiveMessageAsync(
        ReceiveMessageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a message from an SQS queue.
    /// </summary>
    Task<DeleteMessageResponse> DeleteMessageAsync(
        DeleteMessageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the visibility timeout of a message.
    /// </summary>
    Task<ChangeMessageVisibilityResponse> ChangeMessageVisibilityAsync(
        ChangeMessageVisibilityRequest request,
        CancellationToken cancellationToken = default);
}
