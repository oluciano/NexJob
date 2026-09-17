using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace NexJob.Trigger.SalesforceStreaming.Tests;

public sealed class SalesforceStreamingTriggerHandlerTests
{
    private readonly Mock<IScheduler> _mockScheduler = new();
    private readonly Mock<ISalesforceStreamingAuthService> _mockAuthService = new();
    private readonly Mock<ISalesforceBayeuxClient> _mockBayeuxClient = new();
    private readonly InMemoryStreamingReplayIdStore _store = new();
    private readonly IOptions<NexJobOptions> _nexJobOptions = Microsoft.Extensions.Options.Options.Create(new NexJobOptions());

    [Fact]
    public async Task ExecuteAsync_HappyPath_ReceivesEventEnqueuesAndCommitsReplayId()
    {
        // Arrange
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/data/Order__ChangeEvent",
            TargetQueue = "salesforce-queue",
            Authentication = new SalesforceStreamingAuthOptions
            {
                AuthType = SalesforceStreamingAuthType.SessionId,
                InstanceUrl = "https://test.salesforce.com",
                AccessToken = "tok123",
            },
        };

        _mockAuthService.Setup(a => a.GetTokenAsync(It.IsAny<SalesforceStreamingAuthOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesforceStreamingTokenResult("tok123", "https://test.salesforce.com"));

        _mockBayeuxClient.Setup(b => b.HandshakeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-123");

        _mockBayeuxClient.Setup(b => b.SubscribeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                "/data/Order__ChangeEvent",
                (long)SalesforceStreamingReplayPreset.Latest,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        using var payloadDoc = JsonDocument.Parse("{\"Order\": \"100\"}");
        var eventInput = new SalesforceStreamingEventInput(
            ReplayId: 9988,
            Channel: "/data/Order__ChangeEvent",
            Payload: payloadDoc.RootElement.Clone(),
            CreatedDate: DateTimeOffset.UtcNow,
            EventId: "evt-001");

        using var cts = new CancellationTokenSource();
        var connectCount = 0;

        _mockBayeuxClient.Setup(b => b.ConnectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                connectCount++;
                if (connectCount == 1)
                {
                    return Task.FromResult<IReadOnlyList<SalesforceStreamingEventInput>>(new[] { eventInput, });
                }

                cts.Cancel();
                return Task.FromResult<IReadOnlyList<SalesforceStreamingEventInput>>([]);
            });

        var handler = new SalesforceStreamingTriggerHandler(
            _mockScheduler.Object,
            _mockAuthService.Object,
            _mockBayeuxClient.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            _nexJobOptions,
            NullLogger<SalesforceStreamingTriggerHandler>.Instance,
            _store);

        // Act
        await handler.StartAsync(cts.Token);
        await Task.Delay(100);
        await handler.StopAsync(CancellationToken.None);

        // Assert
        _mockScheduler.Verify(
            s => s.EnqueueAsync(
                It.Is<JobRecord>(j => j.Queue == "salesforce-queue" && j.IdempotencyKey == "/data/Order__ChangeEvent:evt-001"),
                DuplicatePolicy.AllowAfterFailed,
                It.IsAny<CancellationToken>()),
            Times.Once);

        var savedReplayId = await _store.GetReplayIdAsync("/data/Order__ChangeEvent");
        savedReplayId.Should().Be(9988);
    }

    [Fact]
    public async Task ExecuteAsync_ResumesFromStoredReplayId()
    {
        // Arrange
        await _store.SaveReplayIdAsync("/data/Order__ChangeEvent", 12345);

        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/data/Order__ChangeEvent",
            Authentication = new SalesforceStreamingAuthOptions
            {
                AuthType = SalesforceStreamingAuthType.SessionId,
                InstanceUrl = "https://test.salesforce.com",
                AccessToken = "tok123",
            },
        };

        _mockAuthService.Setup(a => a.GetTokenAsync(It.IsAny<SalesforceStreamingAuthOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesforceStreamingTokenResult("tok123", "https://test.salesforce.com"));

        _mockBayeuxClient.Setup(b => b.HandshakeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-123");

        using var cts = new CancellationTokenSource();

        _mockBayeuxClient.Setup(b => b.SubscribeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                "/data/Order__ChangeEvent",
                12345, // Must resume from 12345!
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Callback(() => cts.Cancel());

        var handler = new SalesforceStreamingTriggerHandler(
            _mockScheduler.Object,
            _mockAuthService.Object,
            _mockBayeuxClient.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            _nexJobOptions,
            NullLogger<SalesforceStreamingTriggerHandler>.Instance,
            _store);

        // Act
        await handler.StartAsync(cts.Token);
        await Task.Delay(100);
        await handler.StopAsync(CancellationToken.None);

        // Assert
        _mockBayeuxClient.Verify(
            b => b.SubscribeAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                "/data/Order__ChangeEvent",
                12345,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_EnqueueFails_RoutesToDeadLetterAndDoesNotCommitReplayId()
    {
        // Arrange
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/data/Order__ChangeEvent",
            TargetQueue = "salesforce-queue",
            DeadLetterQueue = "salesforce-dlq",
            Authentication = new SalesforceStreamingAuthOptions
            {
                AuthType = SalesforceStreamingAuthType.SessionId,
                InstanceUrl = "https://test.salesforce.com",
                AccessToken = "tok123",
            },
        };

        _mockAuthService.Setup(a => a.GetTokenAsync(It.IsAny<SalesforceStreamingAuthOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesforceStreamingTokenResult("tok123", "https://test.salesforce.com"));

        _mockBayeuxClient.Setup(b => b.HandshakeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-123");

        using var payloadDoc = JsonDocument.Parse("{}");
        var eventInput = new SalesforceStreamingEventInput(
            ReplayId: 443322,
            Channel: "/data/Order__ChangeEvent",
            Payload: payloadDoc.RootElement.Clone(),
            CreatedDate: DateTimeOffset.UtcNow);

        using var cts = new CancellationTokenSource();

        _mockBayeuxClient.Setup(b => b.ConnectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { eventInput, })
            .Callback(() => cts.Cancel());

        // First call to TargetQueue throws!
        _mockScheduler.Setup(s => s.EnqueueAsync(
                It.Is<JobRecord>(j => j.Queue == "salesforce-queue"),
                It.IsAny<DuplicatePolicy>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage unavailable"));

        var handler = new SalesforceStreamingTriggerHandler(
            _mockScheduler.Object,
            _mockAuthService.Object,
            _mockBayeuxClient.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            _nexJobOptions,
            NullLogger<SalesforceStreamingTriggerHandler>.Instance,
            _store);

        // Act
        await handler.StartAsync(cts.Token);
        await Task.Delay(100);
        await handler.StopAsync(CancellationToken.None);

        // Assert - Routed to DLQ!
        _mockScheduler.Verify(
            s => s.EnqueueAsync(
                It.Is<JobRecord>(j => j.Queue == "salesforce-dlq"),
                DuplicatePolicy.AllowAfterFailed,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Replay ID must NOT be saved!
        var storedId = await _store.GetReplayIdAsync("/data/Order__ChangeEvent");
        storedId.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_SessionExpired_InvalidatesTokenAndRehandshakes()
    {
        // Arrange
        var options = new SalesforceStreamingTriggerOptions
        {
            Channel = "/data/Order__ChangeEvent",
            ReconnectDelay = TimeSpan.FromMilliseconds(10),
            MaxReconnectDelay = TimeSpan.FromMilliseconds(50),
            Authentication = new SalesforceStreamingAuthOptions
            {
                AuthType = SalesforceStreamingAuthType.SessionId,
                InstanceUrl = "https://test.salesforce.com",
                AccessToken = "tok123",
            },
        };

        _mockAuthService.Setup(a => a.GetTokenAsync(It.IsAny<SalesforceStreamingAuthOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SalesforceStreamingTokenResult("tok123", "https://test.salesforce.com"));

        _mockBayeuxClient.Setup(b => b.HandshakeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("client-123");

        using var cts = new CancellationTokenSource();
        var connectAttempt = 0;

        _mockBayeuxClient.Setup(b => b.ConnectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                connectAttempt++;
                if (connectAttempt == 1)
                {
                    throw new SalesforceBayeuxException("Session dead", "403::Unknown client", shouldRehandshake: true);
                }

                cts.Cancel();
                return Task.FromResult<IReadOnlyList<SalesforceStreamingEventInput>>([]);
            });

        var handler = new SalesforceStreamingTriggerHandler(
            _mockScheduler.Object,
            _mockAuthService.Object,
            _mockBayeuxClient.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            _nexJobOptions,
            NullLogger<SalesforceStreamingTriggerHandler>.Instance,
            _store);

        // Act
        await handler.StartAsync(cts.Token);
        await Task.Delay(150);
        await handler.StopAsync(CancellationToken.None);

        // Assert
        _mockAuthService.Verify(a => a.InvalidateToken(), Times.Once);
        _mockBayeuxClient.Verify(b => b.HandshakeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }
}
