using NexJob.Redis;
using NexJob.Storage;
using Testcontainers.Redis;
using Xunit;

namespace NexJob.IntegrationTests;

/// <summary>
/// Runs the full <see cref="StorageProviderTestsBase"/> contract against a real
/// Redis instance spun up via Testcontainers.
/// Requires Docker to be available on the host.
/// </summary>
public sealed class RedisStorageProviderTests : StorageProviderTestsBase, IClassFixture<RedisFixture>
{
    private readonly RedisFixture _fixture;

    public RedisStorageProviderTests(RedisFixture fixture)
    {
        _fixture = fixture;

        var connection = StackExchange.Redis.ConnectionMultiplexer.Connect(_fixture.Container.GetConnectionString());
        connection.GetDatabase().Execute("FLUSHDB");
    }

    protected override Task<(IJobStorage Job, IRecurringStorage Recurring, IDashboardStorage Dashboard, IStorageProvider Full)> CreateStorageAsync()
    {
        var provider = new RedisStorageProvider(
            StackExchange.Redis.ConnectionMultiplexer
                .Connect(_fixture.Container.GetConnectionString())
                .GetDatabase());

        return Task.FromResult<(IJobStorage, IRecurringStorage, IDashboardStorage, IStorageProvider)>((provider, provider, provider, provider));
    }
}
