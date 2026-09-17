using System.Diagnostics.CodeAnalysis;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace NexJob.Trigger.Kafka;

/// <summary>
/// Extension methods for registering NexJob Kafka trigger.
/// </summary>
[ExcludeFromCodeCoverage]
public static class KafkaNexJobExtensions
{
    /// <summary>
    /// Adds a Kafka trigger to NexJob that polls a topic and enqueues messages as jobs.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure the <see cref="KafkaTriggerOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNexJobKafkaTrigger(
        this IServiceCollection services,
        Action<KafkaTriggerOptions> configure)
    {
        services.AddOptions<KafkaTriggerOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IKafkaConsumer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<KafkaTriggerOptions>>().Value;
            var config = new ConsumerConfig
            {
                BootstrapServers = options.BootstrapServers,
                GroupId = options.GroupId,
                AutoOffsetReset = AutoOffsetReset.Earliest,
                EnableAutoCommit = false, // CRITICAL — manual commit only
            };
            var consumer = new ConsumerBuilder<string, string>(config).Build();
            return new ConfluentKafkaConsumer(consumer, options.BootstrapServers);
        });

        services.AddHostedService<KafkaTriggerHandler>();

        return services;
    }
}
