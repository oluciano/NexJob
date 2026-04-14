using Amazon.SQS.Model;

namespace NexJob.Trigger.AwsSqs.Tests;

/// <summary>
/// Mock SQS client for testing. Simulates message delivery and tracks delete/visibility calls.
/// </summary>
internal sealed class MockSqsClient : ISqsClient
{
    private readonly Queue<Message> _testMessages = new();
    private readonly object _lock = new();

    public TimeSpan SimulateProcessingDelay { get; set; } = TimeSpan.Zero;
    public bool BlockOnReceive { get; set; }

    public List<string> DeleteCalls { get; } = new();
    public int VisibilityExtensionCalls { get; private set; }

    /// <summary>
    /// Adds a test message to the mock queue.
    /// </summary>
    public void AddTestMessage(Message message)
    {
        lock (_lock)
        {
            _testMessages.Enqueue(message);
        }
    }

    /// <inheritdoc/>
    public async Task<ReceiveMessageResponse> ReceiveMessageAsync(
        ReceiveMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (BlockOnReceive)
        {
            var tcs = new TaskCompletionSource<ReceiveMessageResponse>();
            cancellationToken.Register(() => tcs.TrySetResult(new ReceiveMessageResponse()));
            return await tcs.Task.ConfigureAwait(false);
        }

        var messages = new List<Message>();
        lock (_lock)
        {
            var count = Math.Min(request.MaxNumberOfMessages, _testMessages.Count);
            for (var i = 0; i < count; i++)
            {
                messages.Add(_testMessages.Dequeue());
            }
        }

        return new ReceiveMessageResponse { Messages = messages };
    }

    /// <inheritdoc/>
    public async Task<DeleteMessageResponse> DeleteMessageAsync(
        DeleteMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        DeleteCalls.Add(request.ReceiptHandle);
        await Task.Delay(SimulateProcessingDelay, cancellationToken).ConfigureAwait(false);
        return new DeleteMessageResponse();
    }

    /// <inheritdoc/>
    public async Task<ChangeMessageVisibilityResponse> ChangeMessageVisibilityAsync(
        ChangeMessageVisibilityRequest request,
        CancellationToken cancellationToken = default)
    {
        VisibilityExtensionCalls++;
        await Task.Delay(SimulateProcessingDelay, cancellationToken).ConfigureAwait(false);
        return new ChangeMessageVisibilityResponse();
    }
}
