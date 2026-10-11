using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NexJob.Configuration;
using NexJob.Storage;
using StackExchange.Redis;

namespace NexJob.Redis;

/// <summary>
/// Extension methods for registering the Redis storage provider with NexJob.
/// </summary>
[ExcludeFromCodeCoverage]
public static class NexJobRedisExtensions
{
    /// <summary>
    /// Registers <see cref="RedisStorageProvider"/> as the <see cref="IStorageProvider"/>
    /// for NexJob. Call this <em>before</em> <c>AddNexJob()</c>.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionString">StackExchange.Redis connection string.</param>
    /// <exception cref="InvalidOperationException">The connection string points at a Redis Cluster, which is not supported.</exception>
    public static IServiceCollection AddNexJobRedis(
        this IServiceCollection services,
        string connectionString)
    {
        var mux = ConnectionMultiplexer.Connect(connectionString);
        try
        {
            return services.AddNexJobRedis((IConnectionMultiplexer)mux);
        }
        catch
        {
            mux.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Registers <see cref="RedisStorageProvider"/> as the <see cref="IStorageProvider"/>
    /// for NexJob using an existing <see cref="IConnectionMultiplexer"/>.
    /// Call this <em>before</em> <c>AddNexJob()</c>.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="multiplexer">An already-connected Redis multiplexer.</param>
    /// <exception cref="InvalidOperationException">The multiplexer is connected to a Redis Cluster, which is not supported.</exception>
    public static IServiceCollection AddNexJobRedis(
        this IServiceCollection services,
        IConnectionMultiplexer multiplexer)
    {
        ArgumentNullException.ThrowIfNull(multiplexer);

        if (Array.Exists(multiplexer.GetServers(), server => server.ServerType == ServerType.Cluster))
        {
            throw new InvalidOperationException(
                "NexJob.Redis does not support Redis Cluster: its Lua scripts touch keys they do not declare, and a cluster rejects that. Use a standalone Redis or a primary with replicas. See https://github.com/oluciano/NexJob/issues/479.");
        }

        var db = multiplexer.GetDatabase();
        services.TryAddSingleton(db);
        services.AddSingleton(sp => new RedisStorageProvider(db, sp.GetService<ILogger<RedisStorageProvider>>()));
        services.AddSingleton<IStorageProvider>(sp => sp.GetRequiredService<RedisStorageProvider>());
        services.AddSingleton<IJobStorage>(sp => sp.GetRequiredService<RedisStorageProvider>());
        services.AddSingleton<IRecurringStorage>(sp => sp.GetRequiredService<RedisStorageProvider>());
        services.AddSingleton<IDashboardStorage>(sp => sp.GetRequiredService<RedisStorageProvider>());

        services.AddSingleton<IRuntimeSettingsStore>(
            _ => new RedisRuntimeSettingsStore(db));

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
