using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace NexJob.Internal.Tests;

/// <summary>
/// Hardening unit tests for <see cref="DefaultDeadLetterDispatcher"/>.
/// Targets 100% branch coverage for dead-letter handling logic.
/// </summary>
public sealed class DefaultDeadLetterDispatcherHardeningTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly Mock<IServiceScope> _scope = new();
    private readonly ServiceCollection _services = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultDeadLetterDispatcherHardeningTests"/> class.
    /// </summary>
    public DefaultDeadLetterDispatcherHardeningTests()
    {
        _scopeFactory.Setup(x => x.CreateScope()).Returns(_scope.Object);
    }

    private DefaultDeadLetterDispatcher CreateSut()
    {
        var sp = _services.BuildServiceProvider();
        _scope.Setup(x => x.ServiceProvider).Returns(sp);
        return new DefaultDeadLetterDispatcher(_scopeFactory.Object, NullLogger<DefaultDeadLetterDispatcher>.Instance);
    }

    /// <summary>Tests that dispatcher correctly resolves and invokes the registered handler.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task DispatchAsync_WithRegisteredHandler_InvokesIt()
    {
        // Arrange
        var handlerMock = new Mock<IDeadLetterHandler<TestJob>>();
        _services.AddSingleton(handlerMock.Object);
        var sut = CreateSut();
        var job = new JobRecord { Id = JobId.New(), JobType = typeof(TestJob).AssemblyQualifiedName! };
        var ex = new Exception("failure");

        // Act
        await sut.DispatchAsync(job, ex, CancellationToken.None);

        // Assert
        handlerMock.Verify(x => x.HandleAsync(job, ex, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Tests that dispatcher does not throw when no handler is registered.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task DispatchAsync_NoHandler_DoesNotThrow()
    {
        // Arrange
        var sut = CreateSut();
        var job = new JobRecord { Id = JobId.New(), JobType = typeof(TestJob).AssemblyQualifiedName! };

        // Act
        Func<Task> act = () => sut.DispatchAsync(job, new Exception(), CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    /// <summary>Tests that dispatcher protects itself from user handler exceptions.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task DispatchAsync_HandlerThrows_SwallowsException()
    {
        // Arrange
        var handlerMock = new Mock<IDeadLetterHandler<TestJob>>();
        handlerMock.Setup(x => x.HandleAsync(It.IsAny<JobRecord>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Handler exploded"));

        _services.AddSingleton(handlerMock.Object);
        var sut = CreateSut();
        var job = new JobRecord { Id = JobId.New(), JobType = typeof(TestJob).AssemblyQualifiedName! };

        // Act
        Func<Task> act = () => sut.DispatchAsync(job, new Exception(), CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync("DLT failure must never crash the dispatcher.");
    }

    /// <summary>Tests that dispatcher handles cases where job type cannot be resolved.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task DispatchAsync_InvalidJobType_DoesNotThrow()
    {
        // Arrange
        var sut = CreateSut();
        var job = new JobRecord { Id = JobId.New(), JobType = "InvalidType" };

        // Act
        Func<Task> act = () => sut.DispatchAsync(job, new Exception(), CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    /// <summary>Support job.</summary>
    public sealed class TestJob : IJob
    {
        /// <inheritdoc/>
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
