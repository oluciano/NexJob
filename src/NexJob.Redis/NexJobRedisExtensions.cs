using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton<IStorageProvider>(_ =>
        {
            var mux = ConnectionMultiplexer.Connect(connectionString);
            return new RedisStorageProvider(mux.GetDatabase());
        });

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
        services.AddSingleton<IStorageProvider>(
            _ => new RedisStorageProvider(multiplexer.GetDatabase()));

        return services;
    }
}
