using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace NexJob.Postgres;

/// <summary>
/// Logs, once at startup, the connection pool size NexJob will use against the database and warns when it is
/// smaller than the worker count. Databases are usually shared with other applications, so this makes the
/// footprint visible to whoever operates the service. It never changes behaviour and never blocks startup.
/// </summary>
internal sealed class PostgresPoolAdvisor : IHostedService
{
    private readonly NexJobOptions _options;
    private readonly ILogger<PostgresPoolAdvisor> _logger;
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresPoolAdvisor"/> class.
    /// </summary>
    /// <param name="options">The NexJob options, used for the worker count.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="connectionString">The connection string whose pool settings are reported.</param>
    public PostgresPoolAdvisor(NexJobOptions options, ILogger<PostgresPoolAdvisor> logger, string connectionString)
    {
        _options = options;
        _logger = logger;
        _connectionString = connectionString;
    }

    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // An ops-only host (Workers = 0) runs no jobs, so there is nothing to size.
        if (_options.Workers <= 0)
        {
            return Task.CompletedTask;
        }

        int maxPoolSize;
        try
        {
            maxPoolSize = new NpgsqlConnectionStringBuilder(_connectionString).MaxPoolSize;
        }
        catch (ArgumentException)
        {
            // The provider reports an invalid connection string itself.
            return Task.CompletedTask;
        }

        if (maxPoolSize < _options.Workers)
        {
            _logger.LogWarning(
                "NexJob database pool (PostgreSQL): Maximum Pool Size ({MaxPoolSize}) is smaller than Workers ({Workers}), so workers will wait for connections. Raise Maximum Pool Size in the connection string or lower Workers.",
                maxPoolSize,
                _options.Workers);
        }
        else
        {
            _logger.LogInformation(
                "NexJob database pool (PostgreSQL): Maximum Pool Size = {MaxPoolSize}, Workers = {Workers}. A node needs about Workers + 5 connections; multiply by the number of nodes and keep the total below the database's connection limit, which other applications share.",
                maxPoolSize,
                _options.Workers);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
