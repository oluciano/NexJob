using FluentAssertions;
using Moq;
using NexJob.Redis;
using StackExchange.Redis;
using Xunit;

namespace NexJob.Internal.Tests;

public sealed class DistributedThrottleTests
{
    private readonly Mock<IDatabase> _redis = new();
    private readonly RedisDistributedThrottleStore _sut;

    public DistributedThrottleTests()
    {
        _sut = new RedisDistributedThrottleStore(_redis.Object);
    }

    [Fact]
    public async Task TryAcquireAsync_UnderLimit_ReturnsTrue()
    {
        // Arrange
        _redis.Setup(x => x.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(1));

        // Act
        var result = await _sut.TryAcquireAsync("res", 5);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_AtLimit_ReturnsFalse()
    {
        // Arrange
        _redis.Setup(x => x.ScriptEvaluateAsync(It.IsAny<string>(), It.IsAny<RedisKey[]>(), It.IsAny<RedisValue[]>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create(0));

        // Act
        var result = await _sut.TryAcquireAsync("res", 5);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ThrottleRegistry_WithoutDistributedStore_BehaviorUnchanged()
    {
        // Arrange
        var registry = new ThrottleRegistry();

        // Act & Assert
        (await registry.TryAcquireAsync("res", 1, CancellationToken.None)).Should().BeTrue();
        (await registry.TryAcquireAsync("res", 1, CancellationToken.None)).Should().BeFalse();

        await registry.ReleaseAsync("res", CancellationToken.None);
        (await registry.TryAcquireAsync("res", 1, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task ThrottleRegistry_WithDistributedStore_DelegatesToStore()
    {
        // Arrange
        var store = new Mock<IDistributedThrottleStore>();
        store.Setup(x => x.TryAcquireAsync("res", 5, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var registry = new ThrottleRegistry(store.Object);

        // Act
        var result = await registry.TryAcquireAsync("res", 5, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
        store.Verify(x => x.TryAcquireAsync("res", 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ThrottleRegistry_WithDistributedStore_ReturnsFalseIfStoreRejects()
    {
        // Arrange
        var store = new Mock<IDistributedThrottleStore>();
        store.Setup(x => x.TryAcquireAsync("res", 5, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var registry = new ThrottleRegistry(store.Object);

        // Act
        var result = await registry.TryAcquireAsync("res", 5, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ThrottleRegistry_ReleaseAsync_ReleasesBoth()
    {
        // Arrange
        var store = new Mock<IDistributedThrottleStore>();
        var registry = new ThrottleRegistry(store.Object);

        // Act
        await registry.ReleaseAsync("res", CancellationToken.None);

        // Assert
        store.Verify(x => x.ReleaseAsync("res", It.IsAny<CancellationToken>()), Times.Once);
    }
}
