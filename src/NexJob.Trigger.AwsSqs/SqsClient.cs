using System.Diagnostics.CodeAnalysis;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace NexJob.Trigger.AwsSqs;

/// <summary>
/// Default implementation of <see cref="ISqsClient"/> backed by <see cref="IAmazonSQS"/>.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class SqsClient : ISqsClient
{
    private readonly IAmazonSQS _client;

    /// <summary>
    /// Initializes a new <see cref="SqsClient"/>.
    /// </summary>
    public SqsClient(IAmazonSQS client) => _client = client;

    /// <inheritdoc/>
    public Task<ReceiveMessageResponse> ReceiveMessageAsync(
        ReceiveMessageRequest request,
        CancellationToken cancellationToken = default)
        => _client.ReceiveMessageAsync(request, cancellationToken);

    /// <inheritdoc/>
    public Task<DeleteMessageResponse> DeleteMessageAsync(
        DeleteMessageRequest request,
        CancellationToken cancellationToken = default)
        => _client.DeleteMessageAsync(request, cancellationToken);

    /// <inheritdoc/>
    public Task<ChangeMessageVisibilityResponse> ChangeMessageVisibilityAsync(
        ChangeMessageVisibilityRequest request,
        CancellationToken cancellationToken = default)
        => _client.ChangeMessageVisibilityAsync(request, cancellationToken);
}
