using Avro;
using Avro.Generic;
using Avro.IO;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NexJob.Trigger.Salesforce.Tests;

public sealed class SalesforceSchemaServiceTests
{
    private const string SampleSchemaJson = """
    {
        "type": "record",
        "name": "OrderEvent",
        "namespace": "com.salesforce.test",
        "fields": [
            { "name": "OrderId", "type": "string" },
            { "name": "Amount", "type": "double" }
        ]
    }
    """;

    [Fact]
    public async Task DecodePayloadToJsonAsync_PreRegisteredSchema_DecodesValidJson()
    {
        // Arrange
        var service = new SalesforceSchemaService(null, NullLogger<SalesforceSchemaService>.Instance);
        var schemaId = "schema-order-001";
        service.RegisterSchema(schemaId, SampleSchemaJson);

        var payloadBytes = CreateAvroPayload("ORD-12345", 150.75);

        // Act
        var json = await service.DecodePayloadToJsonAsync(schemaId, payloadBytes);

        // Assert
        json.Should().NotBeNullOrWhiteSpace();
        json.Should().Contain("\"OrderId\":\"ORD-12345\"");
        json.Should().Contain("\"Amount\":150.75");
    }

    [Fact]
    public async Task DecodePayloadToJsonAsync_EmptyPayload_ReturnsEmptyJsonObject()
    {
        // Arrange
        var service = new SalesforceSchemaService(null, NullLogger<SalesforceSchemaService>.Instance);
        service.RegisterSchema("schema-empty", SampleSchemaJson);

        // Act
        var json = await service.DecodePayloadToJsonAsync("schema-empty", []);

        // Assert
        json.Should().Be("{}");
    }

    [Fact]
    public async Task DecodePayloadToJsonAsync_FetchesSchemaDynamically_WhenNotCached()
    {
        // Arrange
        var schemaId = "schema-dynamic-002";
        var fetcherCallCount = 0;
        Task<string> SchemaFetcher(string id, CancellationToken ct)
        {
            fetcherCallCount++;
            return Task.FromResult(SampleSchemaJson);
        }

        var service = new SalesforceSchemaService(SchemaFetcher, NullLogger<SalesforceSchemaService>.Instance);
        var payloadBytes = CreateAvroPayload("ORD-DYNAMIC", 50.0);

        // Act
        var json1 = await service.DecodePayloadToJsonAsync(schemaId, payloadBytes);
        var json2 = await service.DecodePayloadToJsonAsync(schemaId, payloadBytes);

        // Assert
        fetcherCallCount.Should().Be(1); // Cached after first fetch
        json1.Should().Contain("\"OrderId\":\"ORD-DYNAMIC\"");
        json2.Should().Contain("\"OrderId\":\"ORD-DYNAMIC\"");
    }

    [Fact]
    public async Task DecodePayloadToJsonAsync_UnknownSchemaWithoutFetcher_ThrowsKeyNotFoundException()
    {
        // Arrange
        var service = new SalesforceSchemaService(null, NullLogger<SalesforceSchemaService>.Instance);

        // Act
        Func<Task> act = async () => await service.DecodePayloadToJsonAsync("unregistered-schema", [1, 2, 3]);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task DecodePayloadToJsonAsync_FetcherReturnsEmpty_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = new SalesforceSchemaService((id, ct) => Task.FromResult(string.Empty), NullLogger<SalesforceSchemaService>.Instance);

        // Act
        Func<Task> act = async () => await service.DecodePayloadToJsonAsync("empty-returned-schema", [1, 2, 3]);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DecodePayloadToJsonAsync_CorruptedPayload_ThrowsInvalidOperationException()
    {
        // Arrange
        var service = new SalesforceSchemaService(null, NullLogger<SalesforceSchemaService>.Instance);
        service.RegisterSchema("schema-corrupt", SampleSchemaJson);

        byte[] corruptedBytes = [0xFF, 0xFE, 0x01]; // invalid avro binary

        // Act
        Func<Task> act = async () => await service.DecodePayloadToJsonAsync("schema-corrupt", corruptedBytes);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task DecodePayloadToJsonAsync_InvalidSchemaId_ThrowsArgumentException(string? schemaId)
    {
        var service = new SalesforceSchemaService(null, NullLogger<SalesforceSchemaService>.Instance);
        Func<Task> act = async () => await service.DecodePayloadToJsonAsync(schemaId!, [1, 2]);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DecodePayloadToJsonAsync_NullPayload_ThrowsArgumentNullException()
    {
        var service = new SalesforceSchemaService(null, NullLogger<SalesforceSchemaService>.Instance);
        Func<Task> act = async () => await service.DecodePayloadToJsonAsync("some-schema", null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("", "{}")]
    [InlineData(null, "{}")]
    [InlineData("schema-1", "")]
    [InlineData("schema-1", null)]
    public void RegisterSchema_InvalidArguments_ThrowsArgumentException(string? schemaId, string? schemaJson)
    {
        var service = new SalesforceSchemaService(null, NullLogger<SalesforceSchemaService>.Instance);
        var act = () => service.RegisterSchema(schemaId!, schemaJson!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task DecodePayloadToJsonAsync_RecordWithBytesAndArrayFields_NormalizesCorrectly()
    {
        const string complexSchemaJson = """
            {
              "type": "record",
              "name": "ComplexEvent",
              "namespace": "com.salesforce",
              "fields": [
                { "name": "RawData", "type": "bytes" },
                { "name": "Tags", "type": { "type": "array", "items": "string" } }
              ]
            }
            """;

        var service = new SalesforceSchemaService(null, NullLogger<SalesforceSchemaService>.Instance);
        service.RegisterSchema("complex-schema", complexSchemaJson);

        var schema = Schema.Parse(complexSchemaJson);
        var record = new GenericRecord(schema as RecordSchema);
        record.Add("RawData", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        record.Add("Tags", new string[] { "tag1", "tag2" });

        using var stream = new MemoryStream();
        var encoder = new BinaryEncoder(stream);
        var writer = new GenericDatumWriter<GenericRecord>(schema);
        writer.Write(record, encoder);
        var payloadBytes = stream.ToArray();

        var json = await service.DecodePayloadToJsonAsync("complex-schema", payloadBytes);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        doc.RootElement.GetProperty("RawData").GetString().Should().Be("3q2+7w==");
        doc.RootElement.GetProperty("Tags")[0].GetString().Should().Be("tag1");
        doc.RootElement.GetProperty("Tags")[1].GetString().Should().Be("tag2");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new SalesforceSchemaService(null, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static byte[] CreateAvroPayload(string orderId, double amount)
    {
        var schema = Schema.Parse(SampleSchemaJson);
        var record = new GenericRecord(schema as RecordSchema);
        record.Add("OrderId", orderId);
        record.Add("Amount", amount);

        using var stream = new MemoryStream();
        var encoder = new BinaryEncoder(stream);
        var writer = new GenericDatumWriter<GenericRecord>(schema);
        writer.Write(record, encoder);
        return stream.ToArray();
    }
}
