using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NexJob.Trigger.SalesforceStreaming.Tests;

public sealed class SalesforceStreamingEventJobAndExceptionTests
{
    [Fact]
    public async Task SalesforceStreamingEventJob_ExecuteAsync_CompletesSuccessfully()
    {
        // Arrange
        var job = new SalesforceStreamingEventJob(NullLogger<SalesforceStreamingEventJob>.Instance);
        using var doc = JsonDocument.Parse("{\"test\": true}");
        var input = new SalesforceStreamingEventInput(
            ReplayId: 42,
            Channel: "/topic/Test",
            Payload: doc.RootElement.Clone(),
            CreatedDate: DateTimeOffset.UtcNow,
            EventId: "id-42");

        // Act
        var act = () => job.ExecuteAsync(input, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void SalesforceAuthenticationException_Constructors_PreserveProperties()
    {
        var ex1 = new SalesforceAuthenticationException("auth failed");
        ex1.Message.Should().Be("auth failed");

        var inner = new InvalidOperationException("inner");
        var ex2 = new SalesforceAuthenticationException("auth failed with inner", inner);
        ex2.Message.Should().Be("auth failed with inner");
        ex2.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void SalesforceBayeuxException_Constructors_PreserveProperties()
    {
        var ex1 = new SalesforceBayeuxException("bayeux failed", "403::Unknown client", shouldRehandshake: true);
        ex1.Message.Should().Be("bayeux failed");
        ex1.ErrorCode.Should().Be("403::Unknown client");
        ex1.ShouldRehandshake.Should().BeTrue();

        var inner = new HttpRequestException("net error");
        var ex2 = new SalesforceBayeuxException("bayeux network error", inner, "500::Server error", shouldRehandshake: false);
        ex2.Message.Should().Be("bayeux network error");
        ex2.InnerException.Should().BeSameAs(inner);
        ex2.ErrorCode.Should().Be("500::Server error");
        ex2.ShouldRehandshake.Should().BeFalse();
    }
}
