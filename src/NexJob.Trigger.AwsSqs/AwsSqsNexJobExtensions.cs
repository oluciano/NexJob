using System.Diagnostics.CodeAnalysis;
using Amazon.SQS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace NexJob.Trigger.AwsSqs;

/// <summary>
/// Extension methods for registering AWS SQS trigger with NexJob.
/// </summary>
[ExcludeFromCodeCoverage]
public static class AwsSqsNexJobExtensions
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
        services.AddHostedService<AwsSqsTriggerHandler>();

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

    /// <summary>
    /// Adds AWS SQS trigger to NexJob targeting a specific job.
    /// Messages received from the configured SQS queue will be automatically
    /// enqueued as NexJob jobs of type <typeparamref name="TJob"/>.
    /// </summary>
    /// <typeparam name="TJob">The job type implementing <see cref="IJob{TInput}"/> with a string input.</typeparam>
    /// <param name="services">The service collection to add the trigger to.</param>
    /// <param name="configure">Action to configure trigger options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNexJobAwsSqsTrigger<TJob>(
        this IServiceCollection services,
        Action<AwsSqsTriggerOptions> configure)
        where TJob : class, IJob<string>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddTransient<TJob>();
        return AddNexJobAwsSqsTrigger(services, options =>
        {
            configure(options);
            options.JobName = typeof(TJob).AssemblyQualifiedName!;
        });
    }

    /// <summary>
    /// Adds AWS SQS trigger to NexJob with a pre-configured <see cref="IAmazonSQS"/> client targeting a specific job.
    /// Messages received from the configured SQS queue will be automatically
    /// enqueued as NexJob jobs of type <typeparamref name="TJob"/>.
    /// </summary>
    /// <typeparam name="TJob">The job type implementing <see cref="IJob{TInput}"/> with a string input.</typeparam>
    /// <param name="services">The service collection to add the trigger to.</param>
    /// <param name="sqsClient">The SQS client instance to use.</param>
    /// <param name="configure">Action to configure trigger options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddNexJobAwsSqsTrigger<TJob>(
        this IServiceCollection services,
        IAmazonSQS sqsClient,
        Action<AwsSqsTriggerOptions> configure)
        where TJob : class, IJob<string>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(sqsClient);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddSingleton(sqsClient);
        services.AddTransient<ISqsClient>(sp => new SqsClient(sqsClient));

        return AddNexJobAwsSqsTrigger<TJob>(services, configure);
    }

    /// <summary>
    /// Adds AWS SQS trigger via the fluent <see cref="NexJobBuilder"/>.
    /// </summary>
    /// <param name="builder">The NexJob builder.</param>
    /// <param name="configure">Action to configure trigger options.</param>
    /// <returns>The builder for chaining.</returns>
    public static NexJobBuilder AddAwsSqsTrigger(
        this NexJobBuilder builder,
        Action<AwsSqsTriggerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddNexJobAwsSqsTrigger(configure);
        return builder;
    }

    /// <summary>
    /// Adds AWS SQS trigger via the fluent <see cref="NexJobBuilder"/> targeting a specific job.
    /// </summary>
    /// <typeparam name="TJob">The job type implementing <see cref="IJob{TInput}"/> with a string input.</typeparam>
    /// <param name="builder">The NexJob builder.</param>
    /// <param name="configure">Action to configure trigger options.</param>
    /// <returns>The builder for chaining.</returns>
    public static NexJobBuilder AddAwsSqsTrigger<TJob>(
        this NexJobBuilder builder,
        Action<AwsSqsTriggerOptions> configure)
        where TJob : class, IJob<string>
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddNexJobAwsSqsTrigger<TJob>(configure);
        return builder;
    }
}
