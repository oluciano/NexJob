using Microsoft.Extensions.DependencyInjection;
using NexJob.Storage;

namespace NexJob.SqlServer;

/// <summary>
/// Extension methods for registering the SQL Server storage provider with NexJob.
/// </summary>
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
        services.AddSingleton<IStorageProvider>(_ => new SqlServerStorageProvider(connectionString));
        return services;
    }
}
