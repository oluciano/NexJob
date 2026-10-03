using NexJob.MongoDB;
using NexJob.Postgres;
using NexJob.Redis;
using NexJob.SqlServer;

namespace NexJob.Sample.Providers;

/// <summary>
/// Registers the storage provider named by <c>Sample:Provider</c>. Each branch is the registration you would copy for that
/// provider; <c>InMemory</c> registers nothing, because <c>AddNexJob()</c> uses it when no other storage is registered.
/// </summary>
public static class ProviderSetup
{
    /// <summary>The values <c>Sample:Provider</c> accepts.</summary>
    public static readonly string[] Providers = ["InMemory", "Postgres", "SqlServer", "Redis", "MongoDB"];

    /// <summary>Registers the configured storage provider.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration holding <c>Sample:Provider</c> and the connection strings.</param>
    /// <returns>The provider name that was registered.</returns>
    /// <exception cref="InvalidOperationException">The provider is unknown or its connection string is missing.</exception>
    public static string Register(IServiceCollection services, IConfiguration configuration)
    {
        var provider = Array.Find(Providers, p => string.Equals(p, configuration["Sample:Provider"] ?? "InMemory", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Sample:Provider '{configuration["Sample:Provider"]}' is not supported. Use one of: {string.Join(", ", Providers)}.");

        switch (provider)
        {
            case "Postgres":
                services.AddNexJobPostgres(ConnectionString(configuration, provider, "NexJobPostgres"));
                break;
            case "SqlServer":
                services.AddNexJobSqlServer(ConnectionString(configuration, provider, "NexJobSqlServer"));
                break;
            case "Redis":
                services.AddNexJobRedis(ConnectionString(configuration, provider, "NexJobRedis"));
                break;
            case "MongoDB":
                services.AddNexJobMongoDB(ConnectionString(configuration, provider, "NexJobMongoDB"), "nexjob_sample");
                break;
        }

        return provider;
    }

    private static string ConnectionString(IConfiguration configuration, string provider, string name) =>
        configuration.GetConnectionString(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Sample:Provider is '{provider}' but ConnectionStrings:{name} is not set.");
}
