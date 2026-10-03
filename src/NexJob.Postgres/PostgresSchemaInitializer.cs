using Microsoft.Extensions.Hosting;
using Npgsql;

namespace NexJob.Postgres;

/// <summary>
/// Applies the schema migrations when the host starts, for the registration that takes an <see cref="NpgsqlDataSource"/>
/// the application owns. It runs in <see cref="StartingAsync"/>, which the host calls on every such service before it calls
/// <c>StartAsync</c> on any hosted service, so the schema exists before the dispatcher or the settings store touch it, in
/// whatever order the services were registered.
/// </summary>
internal sealed class PostgresSchemaInitializer : IHostedLifecycleService
{
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>Initializes a new instance of the <see cref="PostgresSchemaInitializer"/> class.</summary>
    /// <param name="dataSource">The data source the migrations run on.</param>
    public PostgresSchemaInitializer(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <inheritdoc/>
    public Task StartingAsync(CancellationToken cancellationToken) =>
        SchemaMigrator.MigrateAsync(_dataSource, cancellationToken);

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
