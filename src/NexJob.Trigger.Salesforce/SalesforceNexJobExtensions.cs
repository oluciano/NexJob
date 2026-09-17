using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NexJob.Trigger.Salesforce;

/// <summary>
/// Extension methods for registering Salesforce Pub/Sub API triggers with NexJob.
/// </summary>
public static class SalesforceNexJobExtensions
{
    /// <summary>
    /// Adds a Salesforce trigger to NexJob that consumes CDC and Platform Events from the configured topic.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration delegate for <see cref="SalesforceTriggerOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceTrigger(
        this IServiceCollection services,
        Action<SalesforceTriggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<SalesforceTriggerOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .Validate(options =>
            {
                var context = new System.ComponentModel.DataAnnotations.ValidationContext(options);
                var results = options.Validate(context);
                return !results.Any();
            }, "SalesforceTriggerOptions validation failed.")
            .ValidateOnStart();

        services.AddHttpClient<ISalesforceTokenProvider, SalesforceTokenProvider>();

        services.TryAddSingleton<IReplayIdStore>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SalesforceTriggerOptions>>().Value;
            return new FileReplayIdStore(options.ReplayStoreDirectory);
        });

        services.TryAddSingleton<ISalesforcePubSubClient, SalesforcePubSubClient>();

        services.TryAddSingleton<ISalesforceSchemaService>(sp =>
        {
            var pubSubClient = sp.GetRequiredService<ISalesforcePubSubClient>();
            var logger = sp.GetRequiredService<ILogger<SalesforceSchemaService>>();
            return new SalesforceSchemaService((schemaId, ct) => pubSubClient.GetSchemaJsonAsync(schemaId, ct), logger);
        });

        services.AddHostedService<SalesforceTriggerHandler>();

        return services;
    }

    /// <summary>
    /// Adds a Salesforce trigger to NexJob that consumes CDC and Platform Events and invokes a specific job.
    /// </summary>
    /// <typeparam name="TJob">The job type implementing <see cref="IJob{TInput}"/> with a string input.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration delegate for <see cref="SalesforceTriggerOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceTrigger<TJob>(
        this IServiceCollection services,
        Action<SalesforceTriggerOptions> configure)
        where TJob : class, IJob<string>
    {
        services.AddTransient<TJob>();
        return services.AddSalesforceTrigger(options =>
        {
            configure(options);
            options.JobType = typeof(TJob).AssemblyQualifiedName;
        });
    }

    /// <summary>
    /// Adds a Salesforce trigger via the fluent <see cref="NexJobBuilder"/>.
    /// </summary>
    /// <param name="builder">The NexJob builder.</param>
    /// <param name="configure">Configuration delegate for <see cref="SalesforceTriggerOptions"/>.</param>
    /// <returns>The builder for chaining.</returns>
    public static NexJobBuilder AddSalesforceTrigger(
        this NexJobBuilder builder,
        Action<SalesforceTriggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSalesforceTrigger(configure);
        return builder;
    }

    /// <summary>
    /// Adds a Salesforce trigger via the fluent <see cref="NexJobBuilder"/> invoking a specific job.
    /// </summary>
    /// <typeparam name="TJob">The job type implementing <see cref="IJob{TInput}"/> with a string input.</typeparam>
    /// <param name="builder">The NexJob builder.</param>
    /// <param name="configure">Configuration delegate for <see cref="SalesforceTriggerOptions"/>.</param>
    /// <returns>The builder for chaining.</returns>
    public static NexJobBuilder AddSalesforceTrigger<TJob>(
        this NexJobBuilder builder,
        Action<SalesforceTriggerOptions> configure)
        where TJob : class, IJob<string>
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSalesforceTrigger<TJob>(configure);
        return builder;
    }
}
