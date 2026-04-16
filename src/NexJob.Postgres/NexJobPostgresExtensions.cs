using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using NexJob.Configuration;
using NexJob.Storage;
using Npgsql;

namespace NexJob.Postgres;

/// <summary>
/// Extension methods for registering the PostgreSQL storage provider with NexJob.
/// </summary>
[ExcludeFromCodeCoverage]
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

    /// <summary>
    /// Configures a separate PostgreSQL connection for dashboard read queries.
    /// Use this when your primary database has a read replica to offload
    /// dashboard metrics and job list queries.
    /// </summary>
    /// <param name="builder">The NexJob builder.</param>
    /// <param name="readReplicaConnectionString">
    /// Connection string pointing to the read replica.
    /// Must have the same schema as the primary database.
    /// </param>
    /// <returns>The builder instance.</returns>
    public static NexJobBuilder UseDashboardReadReplica(
        this NexJobBuilder builder,
        string readReplicaConnectionString)
    {
        // Override only IDashboardStorage with the read replica provider
        // The read replica provider is a separate instance of PostgresStorageProvider
        // configured with the replica connection string.
        builder.Services.AddSingleton<IDashboardStorage>(sp =>
        {
            var options = sp.GetRequiredService<NexJobOptions>();
            var dataSource = NpgsqlDataSource.Create(readReplicaConnectionString);
            return new PostgresStorageProvider(dataSource, options);
        });

        return builder;
    }
}
