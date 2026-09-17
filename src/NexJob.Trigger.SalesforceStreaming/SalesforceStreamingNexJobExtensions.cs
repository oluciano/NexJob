using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace NexJob.Trigger.SalesforceStreaming;

/// <summary>
/// Extension methods for configuring the Salesforce Streaming API trigger in NexJob.
/// </summary>
public static class SalesforceStreamingNexJobExtensions
{
    /// <summary>
    /// Adds a Salesforce Streaming API trigger to the service collection with the default pass-through job.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration delegate.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNexJobSalesforceStreamingTrigger(
        this IServiceCollection services,
        Action<SalesforceStreamingTriggerOptions> configure)
    {
        return services.AddNexJobSalesforceStreamingTrigger<SalesforceStreamingEventJob>(configure);
    }

    /// <summary>
    /// Adds a Salesforce Streaming API trigger to the service collection with a custom job implementation.
    /// </summary>
    /// <typeparam name="TJob">The job type implementing <see cref="IJob{SalesforceStreamingEventInput}"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration delegate.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNexJobSalesforceStreamingTrigger<TJob>(
        this IServiceCollection services,
        Action<SalesforceStreamingTriggerOptions> configure)
        where TJob : class, IJob<SalesforceStreamingEventInput>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<SalesforceStreamingTriggerOptions>()
            .Configure(configure)
            .PostConfigure(opt =>
            {
                opt.JobType = typeof(TJob);
            })
            .ValidateDataAnnotations()
            .Validate(options =>
            {
                var context = new System.ComponentModel.DataAnnotations.ValidationContext(options);
                var results = options.Validate(context);
                return !results.Any();
            }, "SalesforceStreamingTriggerOptions validation failed.")
            .ValidateOnStart();

        services.TryAddTransient<TJob>();

        services.TryAddSingleton<IStreamingReplayIdStore>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SalesforceStreamingTriggerOptions>>().Value;
            return options.ReplayIdStore ?? new FileStreamingReplayIdStore(options.ReplayStoreDirectory);
        });

        services.AddHttpClient<ISalesforceStreamingAuthService, SalesforceStreamingAuthService>();
        services.AddHttpClient<ISalesforceBayeuxClient, SalesforceBayeuxClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(150);
        });

        services.AddHostedService<SalesforceStreamingTriggerHandler>();

        return services;
    }

    /// <summary>
    /// Adds a Salesforce Streaming API trigger to the service collection with the default pass-through job.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration delegate.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceStreamingTrigger(
        this IServiceCollection services,
        Action<SalesforceStreamingTriggerOptions> configure)
    {
        return services.AddNexJobSalesforceStreamingTrigger<SalesforceStreamingEventJob>(configure);
    }

    /// <summary>
    /// Adds a Salesforce Streaming API trigger to the service collection with a custom job implementation.
    /// </summary>
    /// <typeparam name="TJob">The job type implementing <see cref="IJob{SalesforceStreamingEventInput}"/>.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configuration delegate.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSalesforceStreamingTrigger<TJob>(
        this IServiceCollection services,
        Action<SalesforceStreamingTriggerOptions> configure)
        where TJob : class, IJob<SalesforceStreamingEventInput>
    {
        return services.AddNexJobSalesforceStreamingTrigger<TJob>(configure);
    }

    /// <summary>
    /// Adds a Salesforce Streaming API trigger to the <see cref="NexJobBuilder"/> with the default pass-through job.
    /// </summary>
    /// <param name="builder">The NexJob builder.</param>
    /// <param name="configure">Configuration delegate.</param>
    /// <returns>The builder for chaining.</returns>
    public static NexJobBuilder AddSalesforceStreamingTrigger(
        this NexJobBuilder builder,
        Action<SalesforceStreamingTriggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddNexJobSalesforceStreamingTrigger(configure);
        return builder;
    }

    /// <summary>
    /// Adds a Salesforce Streaming API trigger to the <see cref="NexJobBuilder"/> with a custom job implementation.
    /// </summary>
    /// <typeparam name="TJob">The job type implementing <see cref="IJob{SalesforceStreamingEventInput}"/>.</typeparam>
    /// <param name="builder">The NexJob builder.</param>
    /// <param name="configure">Configuration delegate.</param>
    /// <returns>The builder for chaining.</returns>
    public static NexJobBuilder AddSalesforceStreamingTrigger<TJob>(
        this NexJobBuilder builder,
        Action<SalesforceStreamingTriggerOptions> configure)
        where TJob : class, IJob<SalesforceStreamingEventInput>
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddNexJobSalesforceStreamingTrigger<TJob>(configure);
        return builder;
    }
}
