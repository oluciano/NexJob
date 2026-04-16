using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace NexJob.Trigger.RabbitMQ;

/// <summary>
/// Extension methods for registering NexJob RabbitMQ trigger.
/// </summary>
[ExcludeFromCodeCoverage]
public static class RabbitMqNexJobExtensions
{
    /// <summary>
    /// Adds a RabbitMQ trigger to NexJob that consumes messages from a queue and enqueues them as jobs.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure the <see cref="RabbitMqTriggerOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNexJobRabbitMqTrigger(
        this IServiceCollection services,
        Action<RabbitMqTriggerOptions> configure)
    {
        services.AddOptions<RabbitMqTriggerOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IConnectionFactory>(_ => new ConnectionFactory());
        services.AddHostedService<RabbitMqTriggerHandler>();

        return services;
    }
}
