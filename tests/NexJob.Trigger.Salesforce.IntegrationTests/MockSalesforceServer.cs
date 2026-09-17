using System.Text;
using Avro;
using Avro.Generic;
using Avro.IO;
using Eventbus.V1;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace NexJob.Trigger.Salesforce.IntegrationTests;

public sealed class MockSalesforceServer : IAsyncDisposable
{
    public const string TestSchemaJson = """
    {
      "type": "record",
      "name": "OrderChangeEvent",
      "namespace": "com.salesforce.event",
      "fields": [
        { "name": "OrderId", "type": "string" },
        { "name": "Amount", "type": "double" }
      ]
    }
    """;

    private readonly WebApplication _app;
    public string OAuthAddress { get; private set; } = string.Empty;
    public string GrpcAddress { get; private set; } = string.Empty;

    public MockSalesforceServer()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            // Port for OAuth (HTTP/1.1)
            kestrel.Listen(System.Net.IPAddress.Loopback, 0, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1;
            });

            // Port for gRPC (HTTP/2 cleartext / h2c)
            kestrel.Listen(System.Net.IPAddress.Loopback, 0, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        builder.Services.AddGrpc();

        _app = builder.Build();

        _app.MapPost("/services/oauth2/token", () => Results.Ok(new
        {
            access_token = "mock-salesforce-access-token-12345",
            instance_url = "https://mock.salesforce.com",
            id = "https://login.salesforce.com/id/00Dxx0000001gEREAY/005xx000001SvGEAA0",
            token_type = "Bearer",
        }));

        _app.MapGrpcService<MockPubSubService>();
    }

    public async Task StartAsync()
    {
        await _app.StartAsync();
        var server = _app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        var addressesFeature = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
        var addresses = addressesFeature?.Addresses.ToList() ?? _app.Urls.ToList();

        OAuthAddress = addresses[0];
        GrpcAddress = addresses[1];
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    public static byte[] CreateAvroOrderEvent(string orderId, double amount)
    {
        var schema = Schema.Parse(TestSchemaJson);
        var record = new GenericRecord(schema as RecordSchema);
        record.Add("OrderId", orderId);
        record.Add("Amount", amount);

        using var stream = new MemoryStream();
        var encoder = new BinaryEncoder(stream);
        var writer = new GenericDatumWriter<GenericRecord>(schema);
        writer.Write(record, encoder);
        return stream.ToArray();
    }

    public sealed class MockPubSubService : PubSub.PubSubBase
    {
        public override Task<TopicInfo> GetTopic(TopicRequest request, ServerCallContext context)
        {
            return Task.FromResult(new TopicInfo
            {
                TopicName = request.TopicName,
                SchemaId = "schema-order-01",
            });
        }

        public override Task<SchemaInfo> GetSchema(SchemaRequest request, ServerCallContext context)
        {
            return Task.FromResult(new SchemaInfo
            {
                SchemaId = request.SchemaId,
                SchemaJson = TestSchemaJson,
            });
        }

        public override async Task Subscribe(
            IAsyncStreamReader<FetchRequest> requestStream,
            IServerStreamWriter<FetchResponse> responseStream,
            ServerCallContext context)
        {
            // Read initial subscribe request
            if (!await requestStream.MoveNext(context.CancellationToken))
            {
                return;
            }

            _ = requestStream.Current;

            // Send one test event
            var payloadBytes = CreateAvroOrderEvent("ORD-INTEG-999", 199.95);
            var consumerEvent = new ConsumerEvent
            {
                Event = new ProducerEvent
                {
                    Id = "evt-integ-001",
                    SchemaId = "schema-order-01",
                    Payload = ByteString.CopyFrom(payloadBytes),
                    Headers =
                    {
                        new EventHeader
                        {
                            Key = "traceparent",
                            Value = ByteString.CopyFromUtf8("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01"),
                        },
                    },
                },
                ReplayId = ByteString.CopyFrom([0x10, 0x20, 0x30, 0x40]),
            };

            var response = new FetchResponse
            {
                Events = { consumerEvent },
                PendingNumRequested = 99,
            };

            await responseStream.WriteAsync(response);

            // Keep the stream open until client cancels
            try
            {
                while (await requestStream.MoveNext(context.CancellationToken))
                {
                    // Flow control requests received
                }
            }
            catch (OperationCanceledException)
            {
                // Normal disconnect
            }
        }
    }
}
