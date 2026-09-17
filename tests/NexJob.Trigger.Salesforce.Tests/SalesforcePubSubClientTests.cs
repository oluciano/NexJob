using FluentAssertions;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace NexJob.Trigger.Salesforce.Tests;

public sealed class SalesforcePubSubClientTests
{
    private readonly SalesforceTriggerOptions _options = new()
    {
        Topic = "/data/ChangeEvents",
        ClientId = "client123",
        ClientSecret = "secret123",
        PubSubEndpoint = "api.pubsub.salesforce.com:7443",
    };

    private readonly Mock<ISalesforceTokenProvider> _tokenProviderMock = new();

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var act = () => new SalesforcePubSubClient(
            null!,
            _tokenProviderMock.Object,
            NullLogger<SalesforcePubSubClient>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullTokenProvider_ThrowsArgumentNullException()
    {
        var act = () => new SalesforcePubSubClient(
            Options.Create(_options),
            null!,
            NullLogger<SalesforcePubSubClient>.Instance);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new SalesforcePubSubClient(
            Options.Create(_options),
            _tokenProviderMock.Object,
            null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithCustomChannel_DoesNotDisposeExternalChannel()
    {
        using var channel = GrpcChannel.ForAddress("http://localhost:50001");
        var client = new SalesforcePubSubClient(
            Options.Create(_options),
            _tokenProviderMock.Object,
            NullLogger<SalesforcePubSubClient>.Instance,
            channel);

        client.Dispose();
        // Custom channel should not be disposed by client
        channel.State.Should().NotBe(Grpc.Core.ConnectivityState.Shutdown);
    }

    [Fact]
    public void Constructor_WithoutChannel_CreatesOwnedChannelAndDisposesCleanly()
    {
        var client = new SalesforcePubSubClient(
            Options.Create(_options),
            _tokenProviderMock.Object,
            NullLogger<SalesforcePubSubClient>.Instance);

        var act = () => client.Dispose();
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task GetSchemaJsonAsync_InvalidSchemaId_ThrowsArgumentException(string? schemaId)
    {
        using var channel = GrpcChannel.ForAddress("http://localhost:50001");
        using var client = new SalesforcePubSubClient(
            Options.Create(_options),
            _tokenProviderMock.Object,
            NullLogger<SalesforcePubSubClient>.Instance,
            channel);

        var act = async () => await client.GetSchemaJsonAsync(schemaId!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task GetTopicInfoAsync_InvalidTopic_ThrowsArgumentException(string? topic)
    {
        using var channel = GrpcChannel.ForAddress("http://localhost:50001");
        using var client = new SalesforcePubSubClient(
            Options.Create(_options),
            _tokenProviderMock.Object,
            NullLogger<SalesforcePubSubClient>.Instance,
            channel);

        var act = async () => await client.GetTopicInfoAsync(topic!);
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
