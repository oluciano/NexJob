using Microsoft.Extensions.DependencyInjection;
using NexJob.Storage;

namespace NexJob.Postgres;

/// <summary>
/// Extension methods for registering the PostgreSQL storage provider with NexJob.
/// </summary>
public static class NexJobPostgresExtensions
{
    /// <summary>
    /// Registers <see cref="PostgresStorageProvider"/> as the <see cref="IStorageProvider"/>
    /// for NexJob. Call this <em>before</em> <c>AddNexJob()</c>.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionString">Npgsql connection string.</param>
    public static IServiceCollection AddNexJobPostgres(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton<IStorageProvider>(_ => new PostgresStorageProvider(connectionString));
        return services;
    }
}
