using Microsoft.Extensions.DependencyInjection;
using NexJob.Configuration;
using NexJob.Storage;
using StackExchange.Redis;

namespace NexJob.Redis;

/// <summary>
/// Extension methods for registering the Redis storage provider with NexJob.
/// </summary>
public static class NexJobRedisExtensions
{
    /// <summary>
    /// Registers <see cref="RedisStorageProvider"/> as the <see cref="IStorageProvider"/>
    /// for NexJob. Call this <em>before</em> <c>AddNexJob()</c>.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionString">StackExchange.Redis connection string.</param>
    public static IServiceCollection AddNexJobRedis(
        this IServiceCollection services,
        string connectionString)
    {
        var mux = ConnectionMultiplexer.Connect(connectionString);
        services.AddSingleton(_ => new RedisStorageProvider(mux.GetDatabase()));
        services.AddSingleton<IStorageProvider>(sp => sp.GetRequiredService<RedisStorageProvider>());
        services.AddSingleton<IJobStorage>(sp => sp.GetRequiredService<RedisStorageProvider>());
        services.AddSingleton<IRecurringStorage>(sp => sp.GetRequiredService<RedisStorageProvider>());
        services.AddSingleton<IDashboardStorage>(sp => sp.GetRequiredService<RedisStorageProvider>());

        services.AddSingleton<IRuntimeSettingsStore>(_ => new RedisRuntimeSettingsStore(mux.GetDatabase()));

        return services;
    }

    /// <summary>
    /// Registers <see cref="RedisStorageProvider"/> as the <see cref="IStorageProvider"/>
    /// for NexJob using an existing <see cref="IConnectionMultiplexer"/>.
    /// Call this <em>before</em> <c>AddNexJob()</c>.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="multiplexer">An already-connected Redis multiplexer.</param>
    public static IServiceCollection AddNexJobRedis(
        this IServiceCollection services,
        IConnectionMultiplexer multiplexer)
    {
        services.AddSingleton(_ => new RedisStorageProvider(multiplexer.GetDatabase()));
        services.AddSingleton<IStorageProvider>(sp => sp.GetRequiredService<RedisStorageProvider>());
        services.AddSingleton<IJobStorage>(sp => sp.GetRequiredService<RedisStorageProvider>());
        services.AddSingleton<IRecurringStorage>(sp => sp.GetRequiredService<RedisStorageProvider>());
        services.AddSingleton<IDashboardStorage>(sp => sp.GetRequiredService<RedisStorageProvider>());

        services.AddSingleton<IRuntimeSettingsStore>(
            _ => new RedisRuntimeSettingsStore(multiplexer.GetDatabase()));

        return services;
    }

    /// <summary>
    /// Enables global throttle enforcement via Redis across all worker nodes.
    /// Without this, [ThrottleAttribute] limits are per-process only.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The original <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddNexJobDistributedThrottle(this IServiceCollection services)
    {
        services.AddSingleton<IDistributedThrottleStore, RedisDistributedThrottleStore>();
        return services;
    }
}
