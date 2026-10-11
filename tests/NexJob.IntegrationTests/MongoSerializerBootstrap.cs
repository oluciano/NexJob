using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NexJob.MongoDB;

namespace NexJob.IntegrationTests;

/// <summary>
/// Registers NexJob's MongoDB serializers before any test runs. <c>new MongoStorageProvider(database)</c> (used by the
/// contract tests) caches the driver's default <c>JobId</c> serializer, and <c>AddNexJobMongoDB</c> then throws "There is
/// already a different serializer registered for type JobId". An application calls <c>AddNexJobMongoDB</c> at startup,
/// so the tests do the same before the first class runs, whatever the order xUnit picks.
/// </summary>
internal static class MongoSerializerBootstrap
{
#pragma warning disable CA2255 // The module initializer is the point: it must run before any test class.
    [ModuleInitializer]
    internal static void RegisterNexJobSerializers() =>
        new ServiceCollection().AddNexJobMongoDB("mongodb://localhost:27017", "nexjob_bootstrap");
#pragma warning restore CA2255
}
