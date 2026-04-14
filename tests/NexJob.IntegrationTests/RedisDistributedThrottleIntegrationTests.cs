using FluentAssertions;
using NexJob.Redis;
using StackExchange.Redis;
using Xunit;

namespace NexJob.IntegrationTests;

[Collection("Redis")]
public sealed class RedisDistributedThrottleIntegrationTests
{
    private readonly RedisFixture _fixture;

    public RedisDistributedThrottleIntegrationTests(RedisFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TryAcquireAsync_and_ReleaseAsync_WorksWithRealRedis()
    {
        // Arrange
        var connection = await ConnectionMultiplexer.ConnectAsync(_fixture.Container.GetConnectionString());
        var db = connection.GetDatabase();
        var sut = new RedisDistributedThrottleStore(db);
        var resource = $"res_{Guid.NewGuid():N}";

        // Act & Assert
        (await sut.TryAcquireAsync(resource, 2)).Should().BeTrue("first slot");
        (await sut.TryAcquireAsync(resource, 2)).Should().BeTrue("second slot");
        (await sut.TryAcquireAsync(resource, 2)).Should().BeFalse("third slot rejected");

        await sut.ReleaseAsync(resource);
        (await sut.TryAcquireAsync(resource, 2)).Should().BeTrue("slot released, now available");
    }

    [Fact]
    public async Task ReleaseAsync_WhenEmpty_IsIdempotent()
    {
        // Arrange
        var connection = await ConnectionMultiplexer.ConnectAsync(_fixture.Container.GetConnectionString());
        var db = connection.GetDatabase();
        var sut = new RedisDistributedThrottleStore(db);
        var resource = $"res_{Guid.NewGuid():N}";

        // Act
        await sut.ReleaseAsync(resource);
        await sut.ReleaseAsync(resource);

        // Assert
        (await sut.TryAcquireAsync(resource, 1)).Should().BeTrue();
    }
}
