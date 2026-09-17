using FluentAssertions;
using Xunit;

namespace NexJob.Trigger.Salesforce.Tests;

public sealed class SalesforceEventPayloadTests
{
    [Fact]
    public void Properties_SetAndGet_ReturnsExpectedValues()
    {
        var headers = new Dictionary<string, string> { { "traceparent", "00-test-01" } };
        var payload = new SalesforceEventPayload
        {
            Topic = "/data/OrderChangeEvent",
            ReplayIdHex = "00010203",
            SchemaId = "schema-abc",
            PayloadJson = "{\"OrderId\":\"123\"}",
            Headers = headers,
        };

        payload.Topic.Should().Be("/data/OrderChangeEvent");
        payload.ReplayIdHex.Should().Be("00010203");
        payload.SchemaId.Should().Be("schema-abc");
        payload.PayloadJson.Should().Be("{\"OrderId\":\"123\"}");
        payload.Headers.Should().ContainKey("traceparent").WhoseValue.Should().Be("00-test-01");
    }

    [Fact]
    public void DefaultHeaders_InitializedEmpty()
    {
        var payload = new SalesforceEventPayload();
        payload.Headers.Should().NotBeNull();
        payload.Headers.Should().BeEmpty();
    }
}
