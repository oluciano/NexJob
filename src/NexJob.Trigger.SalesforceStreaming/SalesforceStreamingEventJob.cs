using Microsoft.Extensions.Logging;

namespace NexJob.Trigger.SalesforceStreaming;

/// <summary>
/// Default pass-through background job implementation for handling events from the Salesforce Streaming API.
/// </summary>
public sealed class SalesforceStreamingEventJob(ILogger<SalesforceStreamingEventJob> logger)
    : IJob<SalesforceStreamingEventInput>
{
    /// <inheritdoc/>
    public Task ExecuteAsync(SalesforceStreamingEventInput input, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Salesforce Streaming event received on channel '{Channel}' with Replay ID {ReplayId}",
            input.Channel,
            input.ReplayId);

        return Task.CompletedTask;
    }
}
