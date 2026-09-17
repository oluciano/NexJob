using System.ComponentModel.DataAnnotations;

namespace NexJob.Trigger.Salesforce;

/// <summary>
/// Configuration options for the Salesforce Pub/Sub API trigger.
/// </summary>
public sealed class SalesforceTriggerOptions : IValidatableObject
{
    /// <summary>
    /// Gets or sets the Salesforce topic to subscribe to (e.g. "/data/ChangeEvents", "/event/OrderEvent__e").
    /// </summary>
    [Required]
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the target NexJob queue name. Defaults to "salesforce-events".
    /// </summary>
    public string TargetQueue { get; set; } = "salesforce-events";

    /// <summary>
    /// Gets or sets the execution priority for enqueued jobs. Defaults to <see cref="JobPriority.Normal"/>.
    /// </summary>
    public JobPriority JobPriority { get; set; } = JobPriority.Normal;

    /// <summary>
    /// Gets or sets the OAuth2 token endpoint URL.
    /// Defaults to "https://login.salesforce.com/services/oauth2/token".
    /// </summary>
    [Required]
    public string AuthEndpoint { get; set; } = "https://login.salesforce.com/services/oauth2/token";

    /// <summary>
    /// Gets or sets the Salesforce Pub/Sub API gRPC endpoint.
    /// Defaults to "api.pubsub.salesforce.com:7443".
    /// </summary>
    [Required]
    public string PubSubEndpoint { get; set; } = "api.pubsub.salesforce.com:7443";

    /// <summary>
    /// Gets or sets the OAuth2 Connected App Client ID (Consumer Key).
    /// </summary>
    [Required]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OAuth2 Connected App Client Secret (Consumer Secret).
    /// </summary>
    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Salesforce Tenant / Organization 18-character ID.
    /// If omitted, automatically derived from the OAuth2 token response.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the replay preset to use when no saved offset exists.
    /// Defaults to <see cref="SalesforceReplayPreset.Latest"/>.
    /// </summary>
    public SalesforceReplayPreset ReplayPreset { get; set; } = SalesforceReplayPreset.Latest;

    /// <summary>
    /// Gets or sets custom replay ID bytes to use when <see cref="ReplayPreset"/> is <see cref="SalesforceReplayPreset.Custom"/>.
    /// </summary>
    public byte[]? CustomReplayId { get; set; }

    /// <summary>
    /// Gets or sets the policy when an expired or rejected Replay ID is encountered.
    /// Defaults to <see cref="ReplayFallbackPolicy.FailFast"/>.
    /// </summary>
    public ReplayFallbackPolicy FallbackPolicy { get; set; } = ReplayFallbackPolicy.FailFast;

    /// <summary>
    /// Gets or sets the number of events requested per gRPC batch. Defaults to 100.
    /// </summary>
    [Range(1, 500)]
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Gets or sets the directory where the <see cref="FileReplayIdStore"/> persists tokens.
    /// Defaults to "./.nexjob/salesforce".
    /// </summary>
    public string ReplayStoreDirectory { get; set; } = "./.nexjob/salesforce";

    /// <summary>
    /// Gets or sets the assembly-qualified type name of the job to enqueue.
    /// If null, defaults to <see cref="SalesforceEventJob"/>.
    /// </summary>
    public string? JobType { get; set; }

    /// <summary>
    /// Gets or sets optional dead-letter queue name if job enqueueing encounters a persistent failure.
    /// </summary>
    public string? DeadLetterQueue { get; set; }

    /// <inheritdoc/>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(Topic))
        {
            yield return new ValidationResult("Topic is required.", [nameof(Topic)]);
        }
        else if (!Topic.StartsWith('/'))
        {
            yield return new ValidationResult(
                $"Topic '{Topic}' is invalid. Salesforce topics must start with '/' (e.g. '/data/ChangeEvents' or '/event/OrderEvent__e').",
                [nameof(Topic)]);
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            yield return new ValidationResult("ClientId is required.", [nameof(ClientId)]);
        }

        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            yield return new ValidationResult("ClientSecret is required.", [nameof(ClientSecret)]);
        }

        if (!Uri.TryCreate(AuthEndpoint, UriKind.Absolute, out var uri) ||
            (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
        {
            yield return new ValidationResult(
                $"AuthEndpoint '{AuthEndpoint}' must be a valid absolute HTTP/HTTPS URL.",
                [nameof(AuthEndpoint)]);
        }

        if (ReplayPreset == SalesforceReplayPreset.Custom && (CustomReplayId == null || CustomReplayId.Length == 0))
        {
            yield return new ValidationResult(
                "CustomReplayId must be specified when ReplayPreset is set to Custom.",
                [nameof(CustomReplayId)]);
        }
    }
}
