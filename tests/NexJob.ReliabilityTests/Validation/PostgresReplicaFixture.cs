using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Xunit;

namespace NexJob.ReliabilityTests.Validation;

/// <summary>
/// A PostgreSQL primary with a physical streaming replica (a hot standby that rejects writes), for the validation rounds.
/// </summary>
public sealed class PostgresReplicaFixture : IAsyncLifetime
{
    private const string PrimaryAlias = "pg-primary";
    private const string Password = "postgres";
    private const string AllowReplication = "#!/bin/bash\necho \"host replication all all scram-sha-256\" >> \"$PGDATA/pg_hba.conf\"\n";

    private const string ReplicaStart = """
        set -e
        mkdir -p /var/lib/postgresql/data
        chown postgres:postgres /var/lib/postgresql/data
        chmod 700 /var/lib/postgresql/data
        gosu postgres env PGPASSWORD=postgres pg_basebackup -h pg-primary -U postgres -D /var/lib/postgresql/data -R -X stream
        echo "primary_conninfo = 'host=pg-primary user=postgres password=postgres'" >> /var/lib/postgresql/data/postgresql.auto.conf
        exec gosu postgres postgres
        """;

    private readonly INetwork _network = new NetworkBuilder().Build();
    private IContainer? _primary;
    private IContainer? _replica;

    /// <summary>Gets the connection string of the primary.</summary>
    public string PrimaryConnectionString => ConnectionString(_primary!);

    /// <summary>Gets the connection string of the replica.</summary>
    public string ReplicaConnectionString => ConnectionString(_replica!);

    /// <summary>Stops the replica, as in a crash.</summary>
    /// <returns>A task that completes when the replica has stopped.</returns>
    public Task StopReplicaAsync() => _replica!.StopAsync();

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await _network.CreateAsync();
        _primary = new ContainerBuilder()
            .WithImage("postgres:16")
            .WithNetwork(_network)
            .WithNetworkAliases(PrimaryAlias)
            .WithPortBinding(5432, true)
            .WithEnvironment("POSTGRES_PASSWORD", Password)
            .WithEnvironment("POSTGRES_DB", "nexjob")
            .WithResourceMapping(Encoding.UTF8.GetBytes(AllowReplication), "/docker-entrypoint-initdb.d/10-replication.sh", UnixFileModes.UserRead | UnixFileModes.UserWrite | UnixFileModes.UserExecute | UnixFileModes.GroupRead | UnixFileModes.GroupExecute | UnixFileModes.OtherRead | UnixFileModes.OtherExecute)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-h", "localhost", "-U", "postgres"))
            .Build();
        await _primary.StartAsync();

        _replica = new ContainerBuilder()
            .WithImage("postgres:16")
            .WithNetwork(_network)
            .WithPortBinding(5432, true)
            .WithEntrypoint("/bin/bash", "-c")
            .WithCommand(ReplicaStart)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("database system is ready to accept read-only connections"))
            .Build();
        await _replica.StartAsync();
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        if (_replica is not null)
        {
            await _replica.DisposeAsync();
        }

        if (_primary is not null)
        {
            await _primary.DisposeAsync();
        }

        await _network.DisposeAsync();
    }

    private static string ConnectionString(IContainer container) =>
        $"Host={container.Hostname};Port={container.GetMappedPublicPort(5432)};Username=postgres;Password={Password};Database=nexjob;Include Error Detail=true";
}
