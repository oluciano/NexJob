using System.ComponentModel.DataAnnotations;

namespace NexJob.Trigger.GooglePubSub;

/// <summary>
/// Configuration options for the NexJob Google Pub/Sub trigger.
/// </summary>
public sealed class GooglePubSubTriggerOptions
{
    /// <summary>
    /// Gets or sets the Google Cloud Project ID.
    /// </summary>
    [Required]
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Pub/Sub Subscription ID.
    /// </summary>
    [Required]
    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target NexJob queue name. Defaults to "default".
    /// </summary>
    public string TargetQueue { get; set; } = "default";

    /// <summary>
    /// Gets or sets the priority of the enqueued NexJob jobs. Default: Normal.
    /// </summary>
    public JobPriority JobPriority { get; set; } = JobPriority.Normal;

    /// <summary>
    /// Gets or sets an optional emulator host for local development (e.g. "localhost:8085").
    /// When set, connects to the Pub/Sub emulator instead of production.
    /// </summary>
    public string? EmulatorHost { get; set; }
}
