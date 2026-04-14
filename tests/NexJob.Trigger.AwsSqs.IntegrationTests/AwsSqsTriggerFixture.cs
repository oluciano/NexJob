using Testcontainers.LocalStack;
using Xunit;

namespace NexJob.Trigger.AwsSqs.IntegrationTests;

/// <summary>
/// Test fixture for AWS SQS integration tests using LocalStack.
/// </summary>
public sealed class AwsSqsTriggerFixture : IAsyncLifetime
{
    private readonly LocalStackContainer _container = new LocalStackBuilder()
        .WithImage("localstack/localstack:3.0")
        .Build();

    /// <summary>
    /// Gets the connection string for LocalStack.
    /// </summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <inheritdoc/>
    public Task InitializeAsync() => _container.StartAsync();

    /// <inheritdoc/>
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
