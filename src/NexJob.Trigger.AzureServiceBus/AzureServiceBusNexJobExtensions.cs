using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace NexJob.Trigger.AzureServiceBus;

/// <summary>
/// Extension methods for registering Azure Service Bus trigger with NexJob.
/// </summary>
public static class AzureServiceBusNexJobExtensions
{
    /// <summary>
    /// Adds Azure Service Bus trigger to NexJob.
    /// Messages received from the configured queue or topic will be automatically
    /// enqueued as NexJob jobs.
    /// </summary>
    /// <param name="services">The service collection to add the trigger to.</param>
    /// <param name="configure">Action to configure trigger options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNexJobAzureServiceBusTrigger(
        this IServiceCollection services,
        Action<AzureServiceBusTriggerOptions> configure)
    {
        services.AddOptions<AzureServiceBusTriggerOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHostedService<AzureServiceBusTriggerHandler>();

        return services;
    }
}
