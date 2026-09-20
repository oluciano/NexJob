using System;
using System.Threading.Tasks;
using Testcontainers.Redis;
using Xunit;

namespace NexJob.StressTests.Fixtures;

public sealed class RedisStressFixture : IAsyncLifetime
{
    private readonly RedisContainer? _container;
    private readonly string? _configuredConnectionString;

    public RedisStressFixture()
    {
        _configuredConnectionString = Environment.GetEnvironmentVariable("NEXJOB_STRESS_REDIS");
        if (string.IsNullOrWhiteSpace(_configuredConnectionString))
        {
            _container = new RedisBuilder()
                .WithImage("redis:7-alpine")
                .Build();
        }
    }

    public string ConnectionString =>
        _configuredConnectionString ?? _container?.GetConnectionString()
        ?? throw new InvalidOperationException("Redis connection string is not available.");

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
