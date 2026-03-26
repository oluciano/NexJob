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
        services.AddSingleton<IScheduler, DefaultScheduler>();
        services.AddSingleton<ThrottleRegistry>();
        services.AddSingleton<JobCaptureLoggerProvider>();
        services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<JobCaptureLoggerProvider>());
        services.AddHostedService<JobDispatcherService>();
        services.AddHostedService<RecurringJobSchedulerService>();
        services.AddHostedService<OrphanedJobWatcherService>();

        return services;
    }
}
