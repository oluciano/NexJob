using NexJob.Configuration;
using NexJob.Redis;
using StackExchange.Redis;
using Xunit;

namespace NexJob.IntegrationTests;

/// <summary>
/// Tests <see cref="RedisRuntimeSettingsStore"/> contract against a real Redis instance.
/// Requires Docker to be available on the host.
/// </summary>
public sealed class RedisRuntimeSettingsStoreTests
    : RuntimeSettingsStoreTestsBase, IClassFixture<RedisFixture>
{
    private readonly RedisFixture _fixture;

    public RedisRuntimeSettingsStoreTests(RedisFixture fixture)
    {
        _fixture = fixture;
    }

    protected override async Task<IRuntimeSettingsStore> CreateStoreAsync()
    {
        var mux = await ConnectionMultiplexer.ConnectAsync(_fixture.Container.GetConnectionString()).ConfigureAwait(false);
        var db = mux.GetDatabase();

        // Flush to ensure clean state per test run
        await db.ExecuteAsync("FLUSHDB").ConfigureAwait(false);

        return new RedisRuntimeSettingsStore(db);
    }
}
