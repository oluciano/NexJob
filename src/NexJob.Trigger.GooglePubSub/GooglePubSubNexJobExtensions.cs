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

    /// <summary>
    /// Adds a Google Pub/Sub trigger to NexJob that receives messages from a subscription and invokes a specific job.
    /// </summary>
    /// <typeparam name="TJob">The job type implementing <see cref="IJob{TInput}"/> with a string input.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An action to configure the <see cref="GooglePubSubTriggerOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNexJobGooglePubSubTrigger<TJob>(
        this IServiceCollection services,
        Action<GooglePubSubTriggerOptions> configure)
        where TJob : class, IJob<string>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddTransient<TJob>();
        return services.AddNexJobGooglePubSubTrigger(options =>
        {
            configure(options);
            options.JobType = typeof(TJob).AssemblyQualifiedName;
        });
    }

    /// <summary>
    /// Adds a Google Pub/Sub trigger via the fluent <see cref="NexJobBuilder"/>.
    /// </summary>
    /// <param name="builder">The NexJob builder.</param>
    /// <param name="configure">An action to configure the <see cref="GooglePubSubTriggerOptions"/>.</param>
    /// <returns>The NexJob builder for chaining.</returns>
    public static NexJobBuilder AddGooglePubSubTrigger(
        this NexJobBuilder builder,
        Action<GooglePubSubTriggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddNexJobGooglePubSubTrigger(configure);
        return builder;
    }

    /// <summary>
    /// Adds a Google Pub/Sub trigger via the fluent <see cref="NexJobBuilder"/> targeting a specific job.
    /// </summary>
    /// <typeparam name="TJob">The job type implementing <see cref="IJob{TInput}"/> with a string input.</typeparam>
    /// <param name="builder">The NexJob builder.</param>
    /// <param name="configure">An action to configure the <see cref="GooglePubSubTriggerOptions"/>.</param>
    /// <returns>The NexJob builder for chaining.</returns>
    public static NexJobBuilder AddGooglePubSubTrigger<TJob>(
        this NexJobBuilder builder,
        Action<GooglePubSubTriggerOptions> configure)
        where TJob : class, IJob<string>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddNexJobGooglePubSubTrigger<TJob>(configure);
        return builder;
    }
}
