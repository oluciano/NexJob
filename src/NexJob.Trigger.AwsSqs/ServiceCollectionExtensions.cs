using Amazon.SQS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace NexJob.Trigger.AwsSqs;

/// <summary>
/// Extension methods for registering AWS SQS trigger with NexJob.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds AWS SQS trigger to NexJob.
    /// Messages received from the configured SQS queue will be automatically
    /// enqueued as NexJob jobs.
    /// </summary>
    /// <param name="services">The service collection to add the trigger to.</param>
    /// <param name="configure">Action to configure trigger options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNexJobAwsSqsTrigger(
        this IServiceCollection services,
        Action<AwsSqsTriggerOptions> configure)
    {
        services.AddOptions<AwsSqsTriggerOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddTransient<ISqsClient>(_ => new SqsClient(new AmazonSQSClient()));
        services.AddHostedService<AwsSqsTrigger>();

        return services;
    }

    /// <summary>
    /// Adds AWS SQS trigger to NexJob with a pre-configured <see cref="IAmazonSQS"/> client.
    /// Messages received from the configured SQS queue will be automatically
    /// enqueued as NexJob jobs.
    /// </summary>
    /// <param name="services">The service collection to add the trigger to.</param>
    /// <param name="sqsClient">The SQS client instance to use.</param>
    /// <param name="configure">Action to configure trigger options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNexJobAwsSqsTrigger(
        this IServiceCollection services,
        IAmazonSQS sqsClient,
        Action<AwsSqsTriggerOptions> configure)
    {
        services.AddSingleton(sqsClient);
        services.AddTransient<ISqsClient>(sp => new SqsClient(sqsClient));

        return AddNexJobAwsSqsTrigger(services, configure);
    }
}
