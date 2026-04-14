using Testcontainers.RabbitMq;
using Xunit;

namespace NexJob.Trigger.RabbitMQ.IntegrationTests;

/// <summary>
/// Test fixture for RabbitMQ integration tests.
/// </summary>
public sealed class RabbitMqTriggerFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management")
        .WithUsername("guest")
        .WithPassword("guest")
        .Build();

    /// <summary>
    /// Gets the RabbitMQ host name.
    /// </summary>
    public string HostName => _container.Hostname;

    /// <summary>
    /// Gets the RabbitMQ public port.
    /// </summary>
    public int Port => _container.GetMappedPublicPort(5672);

    /// <summary>
    /// Gets the RabbitMQ username.
    /// </summary>
    public static string UserName => "guest";

    /// <summary>
    /// Gets the RabbitMQ password.
    /// </summary>
    public static string Password => "guest";

    /// <inheritdoc/>
    public Task InitializeAsync() => _container.StartAsync();

    /// <inheritdoc/>
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
