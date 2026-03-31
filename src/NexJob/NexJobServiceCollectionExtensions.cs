using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NexJob.Configuration;
using NexJob.Internal;
using NexJob.Storage;

namespace NexJob;

/// <summary>
/// Extension methods for registering NexJob services with the dependency injection container.
/// </summary>
public static class NexJobServiceCollectionExtensions
{
    /// <summary>
    /// Registers all NexJob services required to enqueue and execute background jobs.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">
    /// Optional delegate to customise <see cref="NexJobOptions"/>. When omitted, defaults are used.
    /// </param>
    /// <returns>The original <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// By default the in-memory storage provider is registered. To use a persistent provider,
    /// register your own <see cref="IStorageProvider"/> implementation <em>before</em> calling
    /// this method — <c>TryAdd</c> semantics ensure it will not be overwritten.
    /// </remarks>
    public static IServiceCollection AddNexJob(
        this IServiceCollection services,
        Action<NexJobOptions>? configure = null)
    {
        var options = new NexJobOptions();
        configure?.Invoke(options);
        return RegisterCore(services, options);
    }

    /// <summary>
    /// Registers all NexJob services, reading base configuration from <paramref name="configuration"/>
    /// (the <c>NexJob</c> section of <c>appsettings.json</c>).
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">The application configuration root.</param>
    /// <returns>The original <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddNexJob(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = BuildFromConfiguration(configuration);
        return RegisterCore(services, options);
    }

    /// <summary>
    /// Registers all NexJob services, reading base configuration from <paramref name="configuration"/>
    /// and then applying any code-level overrides via <paramref name="configure"/>.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">The application configuration root.</param>
    /// <param name="configure">Delegate that can override values after appsettings are applied.</param>
    /// <returns>The original <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddNexJob(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<NexJobOptions> configure)
    {
        var options = BuildFromConfiguration(configuration);
        configure(options);
        return RegisterCore(services, options);
    }

    /// <summary>
    /// Scans <paramref name="assembly"/> for all non-abstract classes implementing
    /// <see cref="IJob{TInput}"/> or <see cref="IJob"/> and registers each one as <c>Transient</c>.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The original <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddNexJobJobs(
        this IServiceCollection services,
        Assembly assembly)
    {
        var jobGenericInterface = typeof(IJob<>);
        var jobSimpleInterface = typeof(IJob);

        var jobTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true }
                && (
                    // IJob<TInput>
                    Array.Exists(t.GetInterfaces(), i =>
                        i.IsGenericType && i.GetGenericTypeDefinition() == jobGenericInterface)
                    ||
                    // IJob (no-input)
                    Array.Exists(t.GetInterfaces(), i => i == jobSimpleInterface)
                ));

        foreach (var jobType in jobTypes)
        {
            services.TryAddTransient(jobType);
        }

        return services;
    }

    /// <summary>
    /// Registers an <see cref="IJobMigration{TOld,TNew}"/> implementation and its
    /// associated <see cref="NexJob.Internal.MigrationDescriptor"/> so that
    /// <see cref="NexJob.Internal.MigrationPipeline"/> can automatically upgrade
    /// stored payloads when a job's input type changes between versions.
    /// </summary>
    /// <typeparam name="TOld">The previous (source) input type.</typeparam>
    /// <typeparam name="TNew">The current (target) input type.</typeparam>
    /// <typeparam name="TMigration">The migration implementation.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The original <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddJobMigration<TOld, TNew, TMigration>(
        this IServiceCollection services)
        where TMigration : class, IJobMigration<TOld, TNew>
    {
        services.AddTransient<IJobMigration<TOld, TNew>, TMigration>();
        services.AddSingleton(new MigrationDescriptor(typeof(TOld), typeof(TNew)));
        return services;
    }

    // ── private helpers ───────────────────────────────────────────────────────

    private static NexJobOptions BuildFromConfiguration(IConfiguration configuration)
    {
        var options = new NexJobOptions();
        var settings = configuration
            .GetSection(NexJobSettings.SectionName)
            .Get<NexJobSettings>();

        if (settings is not null)
        {
            options.ApplySettings(settings);
        }

        return options;
    }

    private static IServiceCollection RegisterCore(IServiceCollection services, NexJobOptions options)
    {
        services.AddSingleton(options);
        services.TryAddSingleton<IStorageProvider, InMemoryStorageProvider>();
        services.TryAddSingleton<IRuntimeSettingsStore, InMemoryRuntimeSettingsStore>();
        services.AddSingleton<JobWakeUpChannel>();
        services.AddSingleton<IScheduler, DefaultScheduler>();
        services.AddSingleton<ThrottleRegistry>();
        services.AddSingleton<JobCaptureLoggerProvider>();
        services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<JobCaptureLoggerProvider>());
        services.AddHostedService<JobDispatcherService>();
        services.AddHostedService<RecurringJobSchedulerService>();
        services.AddHostedService<ServerHeartbeatService>();
        services.AddHostedService<OrphanedJobWatcherService>();
        services.AddScoped<MigrationPipeline>();
        services.AddScoped<NexJobHealthCheck>();
        services.AddScoped<IJobContextAccessor, JobContextAccessor>();
        services.AddScoped<IJobContext>(sp =>
            sp.GetRequiredService<IJobContextAccessor>().Context
            ?? throw new InvalidOperationException(
                "IJobContext is only available during job execution. " +
                "Do not resolve it outside of an IJob<TInput>.ExecuteAsync call."));

        return services;
    }
}
