namespace NexJob.Trigger.Salesforce;

/// <summary>
/// Default background job handler for Salesforce events when no custom job is configured.
/// </summary>
public sealed class SalesforceEventJob : IJob<string>
{
    /// <inheritdoc/>
    public Task ExecuteAsync(string input, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
