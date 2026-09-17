using System.ComponentModel.DataAnnotations;

namespace NexJob.Trigger.SalesforceStreaming;

/// <summary>
/// Configuration options for the NexJob Salesforce Streaming API trigger.
/// </summary>
public sealed class SalesforceStreamingTriggerOptions : IValidatableObject
{
    /// <summary>
    /// Gets or sets the Salesforce streaming channel name (e.g. "/data/FF_PickOrder__ChangeEvent", "/topic/InvoiceUpdates", "/event/OrderNotification__e").
    /// Required. Must start with a leading slash.
    /// </summary>
    [Required]
    public string Channel { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the authentication options. Required.
    /// </summary>
    [Required]
    public SalesforceStreamingAuthOptions Authentication { get; set; } = new();

    /// <summary>
    /// Gets or sets the Replay preset defining the starting position when no prior Replay ID is stored.
    /// Defaults to <see cref="SalesforceStreamingReplayPreset.Latest"/> (-1).
    /// </summary>
    public SalesforceStreamingReplayPreset ReplayPreset { get; set; } = SalesforceStreamingReplayPreset.Latest;

    /// <summary>
    /// Gets or sets a specific custom Replay ID to start from when <see cref="ReplayPreset"/> is set to <see cref="SalesforceStreamingReplayPreset.Custom"/>.
    /// </summary>
    public long? CustomReplayId { get; set; }

    /// <summary>
    /// Gets or sets the custom Replay ID store implementation.
    /// If null, a <see cref="FileStreamingReplayIdStore"/> using <see cref="ReplayStoreDirectory"/> is used by default.
    /// </summary>
    public IStreamingReplayIdStore? ReplayIdStore { get; set; }

    /// <summary>
    /// Gets or sets the directory on disk where Replay ID files are stored when using the default file store.
    /// Defaults to "./.nexjob/salesforce-streaming".
    /// </summary>
    public string ReplayStoreDirectory { get; set; } = "./.nexjob/salesforce-streaming";

    /// <summary>
    /// Gets or sets the target NexJob queue name where jobs will be enqueued.
    /// Defaults to "salesforce-streaming".
    /// </summary>
    [Required]
    public string TargetQueue { get; set; } = "salesforce-streaming";

    /// <summary>
    /// Gets or sets the priority assigned to enqueued jobs. Defaults to <see cref="JobPriority.Normal"/>.
    /// </summary>
    public JobPriority JobPriority { get; set; } = JobPriority.Normal;

    /// <summary>
    /// Gets or sets the initial delay before attempting to reconnect after a connection loss.
    /// Defaults to 5 seconds.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the maximum delay between reconnection attempts under exponential backoff.
    /// Defaults to 1 minute.
    /// </summary>
    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the maximum consecutive retries before backing off. Defaults to 5.
    /// </summary>
    [Range(1, 100)]
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Gets or sets the dead-letter queue name where messages will be routed if `IScheduler.EnqueueAsync` fails.
    /// Optional. If not specified, failed enqueues are logged as critical errors.
    /// </summary>
    public string? DeadLetterQueue { get; set; }

    /// <summary>
    /// Gets or sets the Salesforce CometD API version (e.g., "60.0").
    /// Defaults to "60.0".
    /// </summary>
    [Required]
    public string CometdVersion { get; set; } = "60.0";

    /// <summary>
    /// Gets or sets the timeout for CometD long-polling connect requests.
    /// Defaults to 120 seconds.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Gets or sets the job type implementing <see cref="IJob{SalesforceStreamingEventInput}"/>.
    /// Defaults to <see cref="SalesforceStreamingEventJob"/>.
    /// </summary>
    public Type? JobType { get; set; }

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Channel))
        {
            yield return new ValidationResult(
                "Channel cannot be empty.",
                new[] { nameof(Channel), });
        }
        else if (!Channel.StartsWith('/'))
        {
            yield return new ValidationResult(
                "Channel must start with a leading slash (e.g. '/data/Order__ChangeEvent' or '/topic/MyTopic').",
                new[] { nameof(Channel), });
        }

        if (string.IsNullOrWhiteSpace(TargetQueue))
        {
            yield return new ValidationResult(
                "TargetQueue cannot be empty.",
                new[] { nameof(TargetQueue), });
        }

        if (ReconnectDelay <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                "ReconnectDelay must be greater than zero.",
                new[] { nameof(ReconnectDelay), });
        }

        if (MaxReconnectDelay < ReconnectDelay)
        {
            yield return new ValidationResult(
                "MaxReconnectDelay must be greater than or equal to ReconnectDelay.",
                new[] { nameof(MaxReconnectDelay), });
        }

        if (ReplayPreset == SalesforceStreamingReplayPreset.Custom && !CustomReplayId.HasValue)
        {
            yield return new ValidationResult(
                "CustomReplayId must be provided when ReplayPreset is set to Custom.",
                new[] { nameof(CustomReplayId), });
        }

        if (Authentication is null)
        {
            yield return new ValidationResult(
                "Authentication settings are required.",
                new[] { nameof(Authentication), });
        }
        else
        {
            foreach (var result in Authentication.Validate(validationContext))
            {
                yield return result;
            }
        }
    }
}
