using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace NexJob.Internal;

/// <summary>
/// Resolves and applies a chain of <see cref="IJobMigration{TOld,TNew}"/> instances to
/// upgrade a serialized job payload from its stored schema version to the current one.
/// </summary>
internal sealed class MigrationPipeline : IMigrationPipeline
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEnumerable<MigrationDescriptor> _descriptors;

    /// <summary>Initializes a new <see cref="MigrationPipeline"/>.</summary>
    public MigrationPipeline(IServiceProvider serviceProvider, IEnumerable<MigrationDescriptor> descriptors)
    {
        _serviceProvider = serviceProvider;
        _descriptors = descriptors;
    }

    /// <inheritdoc/>
    public string Migrate(string inputJson, int storedVersion, int currentVersion, Type inputType)
    {
        if (storedVersion >= currentVersion)
        {
            return inputJson;
        }

        var migrations = BuildChain(storedVersion, currentVersion, inputType);

        if (migrations.Count == 0)
        {
            return inputJson;
        }

        var migrationInterfaceType = typeof(IJobMigration<,>);

        // Determine the starting type from the first migration's TOld
        var firstIface = migrations[0].GetType().GetInterfaces()
            .First(i => i.IsGenericType && i.GetGenericTypeDefinition() == migrationInterfaceType);
        var currentType = firstIface.GetGenericArguments()[0];

        object? payload = JsonSerializer.Deserialize(inputJson, currentType)
                          ?? throw new InvalidOperationException($"Failed to deserialize payload as {currentType.Name}.");

        foreach (var migration in migrations)
        {
            var migrateMethod = migration.GetType().GetMethod("Migrate")!;
            payload = migrateMethod.Invoke(migration, [payload])!;
        }

        return JsonSerializer.Serialize(payload, inputType);
    }

    private List<object> BuildChain(int storedVersion, int currentVersion, Type targetInputType)
    {
        var migrationInterfaceType = typeof(IJobMigration<,>);
        var chain = new List<object>();
        var currentTargetType = targetInputType;

        for (var version = currentVersion; version > storedVersion; version--)
        {
            var descriptor = _descriptors
                .FirstOrDefault(d => d.NewType == currentTargetType);

            if (descriptor is null)
            {
                break;
            }

            var closedMigrationType = migrationInterfaceType.MakeGenericType(descriptor.OldType, descriptor.NewType);
            var migration = _serviceProvider.GetService(closedMigrationType);

            if (migration is null)
            {
                break;
            }

            chain.Insert(0, migration);
            currentTargetType = descriptor.OldType;
        }

        return chain;
    }
}
