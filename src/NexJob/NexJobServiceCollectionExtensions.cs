using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        services.AddSingleton(options);
        services.TryAddSingleton<IStorageProvider, InMemoryStorageProvider>();
        services.AddSingleton<IScheduler, DefaultScheduler>();
        services.AddSingleton<ThrottleRegistry>();
        services.AddHostedService<JobDispatcherService>();
        services.AddHostedService<RecurringJobSchedulerService>();
        services.AddHostedService<OrphanedJobWatcherService>();

        return services;
    }
}
