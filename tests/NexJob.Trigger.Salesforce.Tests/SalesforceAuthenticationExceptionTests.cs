using FluentAssertions;
using Xunit;

namespace NexJob.Trigger.Salesforce.Tests;

public sealed class SalesforceAuthenticationExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_SetsMessage()
    {
        var ex = new SalesforceAuthenticationException("Invalid credentials");
        ex.Message.Should().Be("Invalid credentials");
    }

    [Fact]
    public void Constructor_WithMessageAndInnerException_SetsBoth()
    {
        var inner = new HttpRequestException("Network failure");
        var ex = new SalesforceAuthenticationException("Auth error", inner);

        ex.Message.Should().Be("Auth error");
        ex.InnerException.Should().BeSameAs(inner);
    }
}
