using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using NexJob.Configuration;
using NexJob.Storage;

namespace NexJob.SqlServer;

/// <summary>
/// Extension methods for registering the SQL Server storage provider with NexJob.
/// </summary>
[ExcludeFromCodeCoverage]
public static class NexJobSqlServerExtensions
{
    /// <summary>
    /// Registers <see cref="SqlServerStorageProvider"/> as the <see cref="IStorageProvider"/>
    /// for NexJob. Call this <em>before</em> <c>AddNexJob()</c>.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionString">Microsoft.Data.SqlClient connection string.</param>
    public static IServiceCollection AddNexJobSqlServer(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddSingleton(_ => new SqlServerStorageProvider(connectionString));
        services.AddSingleton<IStorageProvider>(sp => sp.GetRequiredService<SqlServerStorageProvider>());
        services.AddSingleton<IJobStorage>(sp => sp.GetRequiredService<SqlServerStorageProvider>());
        services.AddSingleton<IRecurringStorage>(sp => sp.GetRequiredService<SqlServerStorageProvider>());
        services.AddSingleton<IDashboardStorage>(sp => sp.GetRequiredService<SqlServerStorageProvider>());

        services.AddSingleton<IRuntimeSettingsStore>(_ => new SqlServerRuntimeSettingsStore(connectionString));
        return services;
    }

    /// <summary>
    /// Configures a separate SQL Server connection for dashboard read queries.
    /// </summary>
    /// <param name="builder">The NexJob builder.</param>
    /// <param name="readReplicaConnectionString">
    /// Connection string pointing to the read replica.
    /// </param>
    /// <returns>The builder instance.</returns>
    public static NexJobBuilder UseDashboardReadReplica(
        this NexJobBuilder builder,
        string readReplicaConnectionString)
    {
        builder.Services.AddSingleton<IDashboardStorage>(sp =>
        {
            var options = sp.GetRequiredService<NexJobOptions>();
            var conn = new SqlConnection(readReplicaConnectionString);
            return new SqlServerStorageProvider(conn, options);
        });

        return builder;
    }
}
