using Microsoft.Extensions.DependencyInjection;
using NexJob.Configuration;
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
        services.AddSingleton(_ => new PostgresStorageProvider(connectionString));
        services.AddSingleton<IStorageProvider>(sp => sp.GetRequiredService<PostgresStorageProvider>());
        services.AddSingleton<IJobStorage>(sp => sp.GetRequiredService<PostgresStorageProvider>());
        services.AddSingleton<IRecurringStorage>(sp => sp.GetRequiredService<PostgresStorageProvider>());
        services.AddSingleton<IDashboardStorage>(sp => sp.GetRequiredService<PostgresStorageProvider>());

        services.AddSingleton<IRuntimeSettingsStore>(_ => new PostgresRuntimeSettingsStore(connectionString));
        return services;
    }
}
