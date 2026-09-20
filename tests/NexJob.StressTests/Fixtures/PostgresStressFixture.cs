using System;
using System.Threading.Tasks;
using Testcontainers.PostgreSql;
using Xunit;

namespace NexJob.StressTests.Fixtures;

public sealed class PostgresStressFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer? _container;
    private readonly string? _configuredConnectionString;

    public PostgresStressFixture()
    {
        _configuredConnectionString = Environment.GetEnvironmentVariable("NEXJOB_STRESS_POSTGRES");
        if (string.IsNullOrWhiteSpace(_configuredConnectionString))
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .Build();
        }
    }

    public string ConnectionString =>
        _configuredConnectionString ?? _container?.GetConnectionString()
        ?? throw new InvalidOperationException("Postgres connection string is not available.");

    public async Task InitializeAsync()
    {
        if (_container != null)
        {
            await _container.StartAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_container != null)
        {
            await _container.DisposeAsync();
        }
    }
}
