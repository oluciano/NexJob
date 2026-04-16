using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using NexJob.Configuration;
using NexJob.Storage;

namespace NexJob.MongoDB;

/// <summary>
/// Extension methods for registering the MongoDB storage provider with NexJob.
/// </summary>
[ExcludeFromCodeCoverage]
public static class MongoNexJobExtensions
{
    private static bool _serializersRegistered;
    private static readonly object _lock = new();

    /// <summary>
    /// Registers <see cref="MongoStorageProvider"/> as the <see cref="IStorageProvider"/>
    /// for NexJob, using the provided connection string and database name.
    /// </summary>
    /// <remarks>
    /// Call this <em>before</em> <c>AddNexJob()</c> so that the provider registration
    /// takes precedence over the default in-memory provider.
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionString">MongoDB connection string (e.g. <c>mongodb://localhost:27017</c>).</param>
    /// <param name="databaseName">Name of the MongoDB database to use. Defaults to <c>nexjob</c>.</param>
    public static IServiceCollection AddNexJobMongoDB(
        this IServiceCollection services,
        string connectionString,
        string databaseName = "nexjob")
    {
        // Register serializers once
        RegisterSerializers();

        services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));
        services.AddSingleton<IMongoDatabase>(sp =>
            sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));
        services.AddSingleton<MongoStorageProvider>();
        services.AddSingleton<IStorageProvider>(sp => sp.GetRequiredService<MongoStorageProvider>());
        services.AddSingleton<IJobStorage>(sp => sp.GetRequiredService<MongoStorageProvider>());
        services.AddSingleton<IRecurringStorage>(sp => sp.GetRequiredService<MongoStorageProvider>());
        services.AddSingleton<IDashboardStorage>(sp => sp.GetRequiredService<MongoStorageProvider>());

        services.AddSingleton<IRuntimeSettingsStore, MongoRuntimeSettingsStore>();

        return services;
    }

    /// <summary>
    /// Registers <see cref="MongoStorageProvider"/> using an existing <see cref="IMongoDatabase"/>.
    /// </summary>
    public static IServiceCollection AddNexJobMongoDB(
        this IServiceCollection services,
        IMongoDatabase database)
    {
        RegisterSerializers();
        services.AddSingleton(database);
        services.AddSingleton<MongoStorageProvider>();
        services.AddSingleton<IStorageProvider>(sp => sp.GetRequiredService<MongoStorageProvider>());
        services.AddSingleton<IJobStorage>(sp => sp.GetRequiredService<MongoStorageProvider>());
        services.AddSingleton<IRecurringStorage>(sp => sp.GetRequiredService<MongoStorageProvider>());
        services.AddSingleton<IDashboardStorage>(sp => sp.GetRequiredService<MongoStorageProvider>());

        services.AddSingleton<IRuntimeSettingsStore, MongoRuntimeSettingsStore>();
        return services;
    }

    private static void RegisterSerializers()
    {
        if (_serializersRegistered)
        {
            return;
        }

        lock (_lock)
        {
            if (_serializersRegistered)
            {
                return;
            }

            BsonSerializer.TryRegisterSerializer(JobIdSerializer.Instance);
            BsonSerializer.TryRegisterSerializer(NullableJobIdSerializer.Instance);
            _serializersRegistered = true;
        }
    }
}
