using System.ComponentModel.DataAnnotations;

namespace NexJob.Trigger.AzureServiceBus;

/// <summary>
/// Configuration options for the Azure Service Bus trigger.
/// </summary>
public sealed class AzureServiceBusTriggerOptions
{
    /// <summary>
    /// Azure Service Bus connection string. Required.
    /// </summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Queue name or topic name. Required.
    /// </summary>
    [Required]
    public string QueueOrTopicName { get; set; } = string.Empty;

    /// <summary>
    /// Subscription name. Required when using a topic. Null for queues.
    /// </summary>
    public string? SubscriptionName { get; set; }

    /// <summary>
    /// Maximum number of concurrent messages being processed. Default: 1.
    /// </summary>
    public int MaxConcurrentMessages { get; set; } = 1;

    /// <summary>
    /// Target NexJob queue name. Defaults to "default".
    /// </summary>
    public string TargetQueue { get; set; } = "default";

    /// <summary>
    /// Job priority for enqueued jobs. Defaults to Normal.
    /// </summary>
    public JobPriority JobPriority { get; set; } = JobPriority.Normal;
}
