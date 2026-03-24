using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexJob.Internal;
using NexJob.Storage;

namespace NexJob;

/// <summary>
/// Configuration options for the NexJob background job system.
/// Pass an <see cref="Action{NexJobOptions}"/> to
/// <see cref="NexJobServiceCollectionExtensions.AddNexJob"/> to customise these values.
/// </summary>
public sealed class NexJobOptions
{
    /// <summary>
    /// Maximum number of jobs that can execute concurrently on this host.
    /// Defaults to <c>10</c>.
    /// </summary>
    public int Workers { get; set; } = 10;

    /// <summary>
    /// Maximum number of execution attempts before a job is moved to the dead-letter
    /// (failed) state. Defaults to <c>10</c>.
    /// </summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>
    /// How often the dispatcher polls for new jobs when none are immediately available.
    /// Defaults to <c>15 seconds</c>.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How often active workers refresh their heartbeat timestamp.
    /// Defaults to <c>30 seconds</c>.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum time allowed between heartbeat updates before a job is considered
    /// orphaned and re-enqueued. Defaults to <c>5 minutes</c>.
    /// </summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Ordered list of queue names that workers on this host will poll.
    /// Queues are drained in the order specified. Defaults to <c>["default"]</c>.
    /// </summary>
    public IReadOnlyList<string> Queues { get; set; } = ["default"];
}

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
