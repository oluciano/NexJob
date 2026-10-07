using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexJob.Internal;
using Xunit;

namespace NexJob.Internal.Tests;

public sealed class DeadLetterDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_HandlerRegistered_InvokesHandler()
    {
        // Arrange
        var job = MakeJob<TestJob>();
        var exception = new InvalidOperationException("failure");
        var handler = new RecordingHandler();
        var dispatcher = MakeDispatcher(services =>
            services.AddSingleton<IDeadLetterHandler<TestJob>>(handler));

        // Act
        await dispatcher.DispatchAsync(job, exception);

        // Assert
        handler.Calls.Should().Be(1);
        handler.ReceivedJob.Should().BeSameAs(job);
        handler.ReceivedException.Should().BeSameAs(exception);
    }

    [Fact]
    public async Task DispatchAsync_NoHandlerRegistered_IsNoOp()
    {
        // Arrange
        var job = MakeJob<TestJob>();
        var dispatcher = MakeDispatcher(_ => { });

        // Act
        Func<Task> act = () => dispatcher.DispatchAsync(job, new InvalidOperationException("failure"));

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DispatchAsync_HandlerThrows_DoesNotPropagate()
    {
        // Arrange
        var job = MakeJob<TestJob>();
        var handler = new ThrowingHandler(new InvalidOperationException("handler failure"));
        var dispatcher = MakeDispatcher(services =>
            services.AddSingleton<IDeadLetterHandler<TestJob>>(handler));

        // Act
        Func<Task> act = () => dispatcher.DispatchAsync(job, new InvalidOperationException("failure"));

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DispatchAsync_UnresolvableJobType_IsNoOp()
    {
        // Arrange
        var job = MakeJob<TestJob>(jobType: "NonExistent.Type, Assembly");
        var dispatcher = MakeDispatcher(_ => { });

        // Act
        Func<Task> act = () => dispatcher.DispatchAsync(job, new InvalidOperationException("failure"));

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DispatchAsync_HandlerThrows_LogsError()
    {
        // Arrange
        var job = MakeJob<TestJob>();
        var handlerException = new InvalidOperationException("handler failure");
        var handler = new ThrowingHandler(handlerException);
        var logger = new Mock<ILogger<DefaultDeadLetterDispatcher>>();
        var dispatcher = MakeDispatcher(
            services => services.AddSingleton<IDeadLetterHandler<TestJob>>(handler),
            logger.Object);

        // Act
        await dispatcher.DispatchAsync(job, new InvalidOperationException("failure"));

        // Assert
        logger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((_, _) => true),
            handlerException,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    private static DefaultDeadLetterDispatcher MakeDispatcher(
        Action<IServiceCollection> configureServices,
        ILogger<DefaultDeadLetterDispatcher>? logger = null)
    {
        var services = new ServiceCollection();
        configureServices(services);
        var provider = services.BuildServiceProvider();
        return new DefaultDeadLetterDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            logger ?? Mock.Of<ILogger<DefaultDeadLetterDispatcher>>());
    }

    private static JobRecord MakeJob<TJob>(string? jobType = null)
    {
        return new JobRecord
        {
            Id = JobId.New(),
            JobType = jobType ?? typeof(TJob).AssemblyQualifiedName!,
            InputType = typeof(NoInput).AssemblyQualifiedName!,
            InputJson = "{}",
            Queue = "default",
            Attempts = 1,
            MaxAttempts = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = JobStatus.Failed,
        };
    }

    private sealed class TestJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingHandler : IDeadLetterHandler<TestJob>
    {
        public int Calls { get; private set; }

        public JobRecord? ReceivedJob { get; private set; }

        public Exception? ReceivedException { get; private set; }

        public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken)
        {
            Calls++;
            ReceivedJob = failedJob;
            ReceivedException = lastException;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IDeadLetterHandler<TestJob>
    {
        private readonly Exception _exception;

        public ThrowingHandler(Exception exception) => _exception = exception;

        public Task HandleAsync(JobRecord failedJob, Exception lastException, CancellationToken cancellationToken) =>
            Task.FromException(_exception);
    }

    public DeadLetterDispatcherTests()
    {
        _hardenedScopefactory.Setup(x => x.CreateScope()).Returns(_hardenedScope.Object);
    }

    private readonly Mock<IServiceScopeFactory> _hardenedScopefactory = new();
    private readonly Mock<IServiceScope> _hardenedScope = new();
    private readonly ServiceCollection _hardenedServices = new();

    private DefaultDeadLetterDispatcher CreateSut()
    {
        var sp = _hardenedServices.BuildServiceProvider();
        _hardenedScope.Setup(x => x.ServiceProvider).Returns(sp);
        return new DefaultDeadLetterDispatcher(_hardenedScopefactory.Object, NullLogger<DefaultDeadLetterDispatcher>.Instance);
    }

    /// <summary>Tests that dispatcher correctly resolves and invokes the registered handler.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task DispatchAsync_WithRegisteredHandler_InvokesIt()
    {
        // Arrange
        var handlerMock = new Mock<IDeadLetterHandler<HardenedTestJob>>();
        _hardenedServices.AddSingleton(handlerMock.Object);
        var sut = CreateSut();
        var job = new JobRecord { Id = JobId.New(), JobType = typeof(HardenedTestJob).AssemblyQualifiedName! };
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
        var job = new JobRecord { Id = JobId.New(), JobType = typeof(HardenedTestJob).AssemblyQualifiedName! };

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
        var handlerMock = new Mock<IDeadLetterHandler<HardenedTestJob>>();
        handlerMock.Setup(x => x.HandleAsync(It.IsAny<JobRecord>(), It.IsAny<Exception>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Handler exploded"));

        _hardenedServices.AddSingleton(handlerMock.Object);
        var sut = CreateSut();
        var job = new JobRecord { Id = JobId.New(), JobType = typeof(HardenedTestJob).AssemblyQualifiedName! };

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
    public sealed class HardenedTestJob : IJob
    {
        /// <inheritdoc/>
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
