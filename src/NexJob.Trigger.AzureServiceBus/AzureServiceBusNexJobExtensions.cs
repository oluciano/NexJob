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
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<AzureServiceBusTriggerOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHostedService<AzureServiceBusTriggerHandler>();

        return services;
    }

    /// <summary>
    /// Adds Azure Service Bus trigger to NexJob targeting a specific job.
    /// Messages received from the configured queue or topic will be automatically
    /// enqueued as NexJob jobs of type <typeparamref name="TJob"/>.
    /// </summary>
    /// <typeparam name="TJob">The job type implementing <see cref="IJob{TInput}"/> with a string input.</typeparam>
    /// <param name="services">The service collection to add the trigger to.</param>
    /// <param name="configure">Action to configure trigger options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNexJobAzureServiceBusTrigger<TJob>(
        this IServiceCollection services,
        Action<AzureServiceBusTriggerOptions> configure)
        where TJob : class, IJob<string>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddTransient<TJob>();
        return services.AddNexJobAzureServiceBusTrigger(options =>
        {
            configure(options);
            options.JobType = typeof(TJob).AssemblyQualifiedName;
        });
    }

    /// <summary>
    /// Adds Azure Service Bus trigger via the fluent <see cref="NexJobBuilder"/>.
    /// </summary>
    /// <param name="builder">The NexJob builder.</param>
    /// <param name="configure">Action to configure trigger options.</param>
    /// <returns>The NexJob builder for chaining.</returns>
    public static NexJobBuilder AddAzureServiceBusTrigger(
        this NexJobBuilder builder,
        Action<AzureServiceBusTriggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddNexJobAzureServiceBusTrigger(configure);
        return builder;
    }

    /// <summary>
    /// Adds Azure Service Bus trigger via the fluent <see cref="NexJobBuilder"/> targeting a specific job.
    /// </summary>
    /// <typeparam name="TJob">The job type implementing <see cref="IJob{TInput}"/> with a string input.</typeparam>
    /// <param name="builder">The NexJob builder.</param>
    /// <param name="configure">Action to configure trigger options.</param>
    /// <returns>The NexJob builder for chaining.</returns>
    public static NexJobBuilder AddAzureServiceBusTrigger<TJob>(
        this NexJobBuilder builder,
        Action<AzureServiceBusTriggerOptions> configure)
        where TJob : class, IJob<string>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddNexJobAzureServiceBusTrigger<TJob>(configure);
        return builder;
    }
}
