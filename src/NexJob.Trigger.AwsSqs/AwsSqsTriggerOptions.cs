using System.ComponentModel.DataAnnotations;

namespace NexJob.Trigger.AwsSqs;

/// <summary>
/// Configuration options for the AWS SQS trigger.
/// </summary>
public sealed class AwsSqsTriggerOptions
{
    /// <summary>
    /// AWS SQS queue URL. Required.
    /// Example: https://sqs.us-east-1.amazonaws.com/123456789/my-queue.
    /// </summary>
    [Required]
    public string QueueUrl { get; set; } = string.Empty;

    /// <summary>
    /// The name of the job type to enqueue (assembly-qualified name).
    /// This is used by the job record factory to construct the job record.
    /// </summary>
    [Required]
    public string JobName { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of messages to receive in a single batch. Valid range: 1–10. Default: 10.
    /// </summary>
    [Range(1, 10)]
    public int MaxMessages { get; set; } = 10;

    /// <summary>
    /// Duration (in seconds) for which the receive call waits for a message to arrive
    /// before returning (long polling). Maximum: 20. Default: 20.
    /// </summary>
    [Range(0, 20)]
    public int WaitTimeSeconds { get; set; } = 20;

    /// <summary>
    /// The duration (in seconds) that a received message is hidden from other consumers.
    /// Default: 30.
    /// </summary>
    [Range(0, 43200)]
    public int VisibilityTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// The interval (in seconds) at which the visibility timeout is extended while a job
    /// is being processed. Should be less than <see cref="VisibilityTimeoutSeconds"/>.
    /// Default: 15.
    /// </summary>
    [Range(1, 43200)]
    public int VisibilityExtensionIntervalSeconds { get; set; } = 15;

    /// <summary>
    /// Target NexJob queue name. Defaults to "default".
    /// </summary>
    public string TargetQueue { get; set; } = "default";

    /// <summary>
    /// Job priority for enqueued jobs. Defaults to Normal.
    /// </summary>
    public JobPriority JobPriority { get; set; } = JobPriority.Normal;
}
