using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Tests;
using Xunit;

namespace NexJob.ReliabilityTests.Validation;

/// <summary>
/// The Kafka trigger against a SASL_SSL listener. Run on demand with
/// <c>dotnet test --filter Category=Reliability.Validation</c>.
/// </summary>
[Trait("Category", "Reliability.Validation")]
public sealed class KafkaSaslTlsValidationTests : IClassFixture<KafkaSaslTlsFixture>
{
    private readonly KafkaSaslTlsFixture _fixture;

    public KafkaSaslTlsValidationTests(KafkaSaslTlsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SaslPlainOverTls_WithTheRightCredentials_TheRecordBecomesAJob()
    {
        var topic = await PublishAsync();
        using var host = BuildHost(topic, KafkaSaslTlsFixture.Password);
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        await TestWait.UntilAsync(
            async () => (await scheduler.GetJobsByTagAsync("trigger:kafka")).Count == 1,
            timeout: TimeSpan.FromSeconds(60),
            because: "the record to cross the SASL_SSL listener and become a job");
        await host.StopAsync();

        (await scheduler.GetJobsByTagAsync("trigger:kafka")).Should().ContainSingle();
    }

    [Fact]
    public async Task SaslPlainOverTls_WithAWrongPassword_NothingIsConsumed()
    {
        var topic = await PublishAsync();
        using var host = BuildHost(topic, "not-the-password");
        await host.StartAsync();
        var scheduler = host.Services.GetRequiredService<IScheduler>();

        // Assert that something did NOT happen: give the consumer time to authenticate and fetch.
        await Task.Delay(TimeSpan.FromSeconds(15));
        await host.StopAsync();

        (await scheduler.GetJobsByTagAsync("trigger:kafka")).Should().BeEmpty("authentication fails before any fetch");
    }

    private IHost BuildHost(string topic, string password) =>
        Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNexJob(options => options.Workers = 0);
                services.AddKafkaTrigger(options =>
                {
                    options.BootstrapServers = _fixture.BootstrapServers;
                    options.Topic = topic;
                    options.GroupId = $"group-{Guid.NewGuid():N}";
                    options.ConfigureConsumer = config =>
                    {
                        config.SecurityProtocol = SecurityProtocol.SaslSsl;
                        config.SaslMechanism = SaslMechanism.Plain;
                        config.SaslUsername = KafkaSaslTlsFixture.User;
                        config.SaslPassword = password;
                        config.SslCaPem = _fixture.CaPem;
                    };
                });
            })
            .Build();

    private async Task<string> PublishAsync()
    {
        var topic = $"sasl-{Guid.NewGuid():N}";
        var secure = new ClientConfig
        {
            BootstrapServers = _fixture.BootstrapServers,
            SecurityProtocol = SecurityProtocol.SaslSsl,
            SaslMechanism = SaslMechanism.Plain,
            SaslUsername = KafkaSaslTlsFixture.User,
            SaslPassword = KafkaSaslTlsFixture.Password,
            SslCaPem = _fixture.CaPem,
        };
        using (var admin = new AdminClientBuilder(secure).Build())
        {
            await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        }

        using var producer = new ProducerBuilder<string, string>(secure).Build();
        var headers = new Headers { { "nexjob.job_type", Encoding.UTF8.GetBytes(typeof(SaslJob).AssemblyQualifiedName!) } };
        await producer.ProduceAsync(topic, new Message<string, string> { Key = "k", Value = "v", Headers = headers });
        return topic;
    }

    private sealed class SaslJob : IJob<string>
    {
        public Task ExecuteAsync(string input, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
