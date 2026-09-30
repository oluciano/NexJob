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
    /// <param name="keepLegacyGlobalDateTimeOffsetSerializer">
    /// Versions before 5.6 registered a global <see cref="global::MongoDB.Bson.Serialization.Serializers.DateTimeOffsetSerializer"/>
    /// (string representation) that also applied to the host application's own <see cref="DateTimeOffset"/> values.
    /// NexJob now scopes it to its own documents. Pass <see langword="true"/> only if your application unknowingly
    /// relies on the old global registration; the flag is temporary and will be removed in a later release.
    /// </param>
    public static IServiceCollection AddNexJobMongoDB(
        this IServiceCollection services,
        string connectionString,
        string databaseName = "nexjob",
        bool keepLegacyGlobalDateTimeOffsetSerializer = false)
    {
        // Register serializers once
        RegisterSerializers();
        KeepLegacyGlobalSerializer(keepLegacyGlobalDateTimeOffsetSerializer);

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
    /// <param name="services">The service collection to configure.</param>
    /// <param name="database">The database to use.</param>
    /// <param name="keepLegacyGlobalDateTimeOffsetSerializer">
    /// See <see cref="AddNexJobMongoDB(IServiceCollection, string, string, bool)"/>.
    /// </param>
    public static IServiceCollection AddNexJobMongoDB(
        this IServiceCollection services,
        IMongoDatabase database,
        bool keepLegacyGlobalDateTimeOffsetSerializer = false)
    {
        RegisterSerializers();
        KeepLegacyGlobalSerializer(keepLegacyGlobalDateTimeOffsetSerializer);
        services.AddSingleton(database);
        services.AddSingleton<MongoStorageProvider>();
        services.AddSingleton<IStorageProvider>(sp => sp.GetRequiredService<MongoStorageProvider>());
        services.AddSingleton<IJobStorage>(sp => sp.GetRequiredService<MongoStorageProvider>());
        services.AddSingleton<IRecurringStorage>(sp => sp.GetRequiredService<MongoStorageProvider>());
        services.AddSingleton<IDashboardStorage>(sp => sp.GetRequiredService<MongoStorageProvider>());

        services.AddSingleton<IRuntimeSettingsStore, MongoRuntimeSettingsStore>();
        return services;
    }

    private static void KeepLegacyGlobalSerializer(bool keep)
    {
        if (!keep)
        {
            return;
        }

        try
        {
            BsonSerializer.TryRegisterSerializer(new global::MongoDB.Bson.Serialization.Serializers.DateTimeOffsetSerializer(global::MongoDB.Bson.BsonType.String));
        }
        catch (global::MongoDB.Bson.BsonSerializationException)
        {
            // The host application already registered its own DateTimeOffset serializer; keep it.
        }
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
