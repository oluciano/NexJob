using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Google.Api.Gax;
using Google.Api.Gax.Grpc;
using Google.Cloud.PubSub.V1;
using Grpc.Core;
using Xunit;

namespace NexJob.Trigger.GooglePubSub.IntegrationTests;

/// <summary>
/// Starts the Google Pub/Sub emulator once for the tests of this project and creates topics and subscriptions on it.
/// </summary>
public sealed class PubSubEmulatorFixture : IAsyncLifetime
{
    /// <summary>The project id used for every topic and subscription.</summary>
    public const string ProjectId = "nexjob-test";

    private const int EmulatorPort = 8085;

    private readonly IContainer _container = new ContainerBuilder()
        .WithImage("gcr.io/google.com/cloudsdktool/cloud-sdk:emulators")
        .WithPortBinding(EmulatorPort, true)
        .WithCommand("gcloud", "beta", "emulators", "pubsub", "start", $"--host-port=0.0.0.0:{EmulatorPort}")
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server started"))
        .Build();

    private PublisherServiceApiClient? _publisherAdmin;
    private SubscriberServiceApiClient? _subscriberAdmin;

    /// <summary>Gets the emulator endpoint as <c>host:port</c>.</summary>
    public string EmulatorHost => $"{_container.Hostname}:{_container.GetMappedPublicPort(EmulatorPort)}";

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _publisherAdmin = await new PublisherServiceApiClientBuilder
        {
            Endpoint = EmulatorHost,
            ChannelCredentials = ChannelCredentials.Insecure,
        }.BuildAsync();
        _subscriberAdmin = await new SubscriberServiceApiClientBuilder
        {
            Endpoint = EmulatorHost,
            ChannelCredentials = ChannelCredentials.Insecure,
        }.BuildAsync();
    }

    /// <inheritdoc/>
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Creates a topic and a pull subscription on it, both with unique names.</summary>
    /// <returns>The topic and subscription names.</returns>
    public async Task<(TopicName Topic, SubscriptionName Subscription)> CreateTopicAndSubscriptionAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var topic = TopicName.FromProjectTopic(ProjectId, $"topic-{suffix}");
        var subscription = SubscriptionName.FromProjectSubscription(ProjectId, $"sub-{suffix}");
        await _publisherAdmin!.CreateTopicAsync(topic);
        await _subscriberAdmin!.CreateSubscriptionAsync(subscription, topic, pushConfig: null, ackDeadlineSeconds: 10);
        return (topic, subscription);
    }

    /// <summary>Publishes one message and returns the id the emulator gave it.</summary>
    /// <param name="topic">The topic.</param>
    /// <param name="data">The message body.</param>
    /// <param name="attributes">Optional message attributes.</param>
    /// <returns>The message id.</returns>
    public async Task<string> PublishAsync(TopicName topic, string data, IDictionary<string, string>? attributes = null)
    {
        var message = new PubsubMessage { Data = Google.Protobuf.ByteString.CopyFromUtf8(data) };
        foreach (var pair in attributes ?? new Dictionary<string, string>())
        {
            message.Attributes[pair.Key] = pair.Value;
        }

        var response = await _publisherAdmin!.PublishAsync(topic, new[] { message });
        return response.MessageIds[0];
    }

    /// <summary>Pulls without waiting: what is still waiting on the subscription (not yet acknowledged).</summary>
    /// <param name="subscription">The subscription.</param>
    /// <returns>The number of messages returned.</returns>
    public async Task<int> PullPendingCountAsync(SubscriptionName subscription)
    {
        try
        {
            var response = await _subscriberAdmin!.PullAsync(
                subscription,
                maxMessages: 10,
                CallSettings.FromExpiration(Expiration.FromTimeout(TimeSpan.FromSeconds(3))));
            return response.ReceivedMessages.Count;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
        {
            return 0; // nothing was waiting
        }
    }
}
