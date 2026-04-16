using System.Diagnostics.CodeAnalysis;
using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace NexJob.Trigger.GooglePubSub;

/// <summary>
/// Extension methods for registering NexJob Google Pub/Sub trigger.
/// </summary>
[ExcludeFromCodeCoverage]
public static class GooglePubSubNexJobExtensions
{
    /// <summary>
    /// Adds a Google Pub/Sub trigger to NexJob that receives messages from a subscription and enqueues them as jobs.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure the <see cref="GooglePubSubTriggerOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNexJobGooglePubSubTrigger(
        this IServiceCollection services,
        Action<GooglePubSubTriggerOptions> configure)
    {
        services.AddOptions<GooglePubSubTriggerOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPubSubSubscriber>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<GooglePubSubTriggerOptions>>().Value;
            var subscriptionName = SubscriptionName.FromProjectSubscription(
                options.ProjectId, options.SubscriptionId);

            var client = SubscriberClient.Create(subscriptionName);

            return new GooglePubSubSubscriber(client);
        });

        services.AddHostedService<GooglePubSubTriggerHandler>();

        return services;
    }
}
