using FluentAssertions;
using NexJob.Dashboard.Pages;
using Xunit;

namespace NexJob.Tests;

public sealed class HelpersTests
{
    [Fact]
    public void ParseTraceParent_AW3CValue_IsParsed()
    {
        // N1 (Positive, #592): a W3C traceparent gives its trace id, span id and sampled flag.
        var (traceId, spanId, sampled) = Helpers.ParseTraceParent("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");

        traceId.Should().Be("0af7651916cd43dd8448eb211c80319c");
        spanId.Should().Be("b7ad6b7169203331");
        sampled.Should().BeTrue();
    }

    [Theory]
    [InlineData("00-<b>x</b>-b7ad6b7169203331-01")]
    [InlineData("00-0af7651916cd43dd8448eb211c80319c-<i>y</i>-01")]
    [InlineData("00-short-b7ad6b7169203331-01")]
    [InlineData("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331")]
    [InlineData("01-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01")]
    public void ParseTraceParent_ANonW3CValue_IsRejected(string value)
    {
        // N2 (Negative, #592): the id and span are hex of the W3C length, so nothing else can reach the page.
        Helpers.ParseTraceParent(value).Should().Be(((string?)null, (string?)null, false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseTraceParent_NullEmptyOrBlank_IsRejected(string? value)
    {
        // N3 (Invalid Input): no value, no card.
        Helpers.ParseTraceParent(value).Should().Be(((string?)null, (string?)null, false));
    }
}
