using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NexJob.Tests;
using RabbitMQ.Client;
using Xunit;

namespace NexJob.Trigger.RabbitMQ.IntegrationTests;

/// <summary>
/// The trigger against a broker with a TLS listener: the <c>ConfigureConnection</c> delegate reaches the connection,
/// and a certificate that does not match the server name is refused.
/// </summary>
[Collection("RabbitMQ")]
public sealed class RabbitMqTlsTests : IClassFixture<RabbitMqTlsFixture>
{
    private readonly RabbitMqTlsFixture _fixture;

    public RabbitMqTlsTests(RabbitMqTlsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TlsListener_WithTheRightServerName_TheMessageBecomesAJob()
    {
        var queue = await PublishAsync("tls-ok");
        await using var provider = BuildTrigger(queue, serverName: "localhost", port: _fixture.TlsPort);
        var trigger = provider.GetRequiredService<IHostedService>();
        var scheduler = provider.GetRequiredService<IScheduler>();

        await trigger.StartAsync(CancellationToken.None);
        JobRecord? job = null;
        await TestWait.UntilAsync(
            async () =>
            {
                job = (await scheduler.GetJobsByTagAsync("trigger:rabbitmq")).FirstOrDefault();
                return job is not null;
            },
            because: "the message to cross the TLS connection and become a job");
        await trigger.StopAsync(CancellationToken.None);

        job!.JobType.Should().Be(typeof(TestJob).AssemblyQualifiedName);
    }

    [Fact]
    public async Task TlsListener_WithAServerNameThatDoesNotMatchTheCertificate_NothingIsConsumed()
    {
        var queue = await PublishAsync("tls-wrong-name");
        await using var provider = BuildTrigger(queue, serverName: "other.example.com", port: _fixture.TlsPort);
        var trigger = provider.GetRequiredService<IHostedService>();
        var scheduler = provider.GetRequiredService<IScheduler>();

        await trigger.StartAsync(CancellationToken.None);
        // Assert that something did NOT happen: give the trigger several reconnect attempts.
        await Task.Delay(TimeSpan.FromSeconds(5));
        await trigger.StopAsync(CancellationToken.None);

        (await scheduler.GetJobsByTagAsync("trigger:rabbitmq")).Should().BeEmpty("the TLS handshake is refused");
        (await CountMessagesAsync(queue)).Should().Be(1, "the message is still in the queue");
    }

    [Fact]
    public async Task TlsRequestedOnThePlainPort_NothingIsConsumed()
    {
        var queue = await PublishAsync("tls-on-plain-port");
        await using var provider = BuildTrigger(queue, serverName: "localhost", port: _fixture.PlainPort);
        var trigger = provider.GetRequiredService<IHostedService>();
        var scheduler = provider.GetRequiredService<IScheduler>();

        await trigger.StartAsync(CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(5));
        await trigger.StopAsync(CancellationToken.None);

        (await scheduler.GetJobsByTagAsync("trigger:rabbitmq")).Should().BeEmpty();
        (await CountMessagesAsync(queue)).Should().Be(1);
    }

    private ConnectionFactory PlainFactory() => new()
    {
        HostName = _fixture.HostName,
        Port = _fixture.PlainPort,
        UserName = "guest",
        Password = "guest",
    };

    private Task<string> PublishAsync(string queue)
    {
        using var connection = PlainFactory().CreateConnection();
        using var channel = connection.CreateModel();
        channel.QueueDeclare(queue, durable: false, exclusive: false, autoDelete: false);
        var props = channel.CreateBasicProperties();
        props.MessageId = queue;
        props.Headers = new Dictionary<string, object> { ["nexjob.job_type"] = typeof(TestJob).AssemblyQualifiedName! };
        channel.BasicPublish(string.Empty, queue, props, Encoding.UTF8.GetBytes("{}"));
        return Task.FromResult(queue);
    }

    private Task<uint> CountMessagesAsync(string queue)
    {
        using var connection = PlainFactory().CreateConnection();
        using var channel = connection.CreateModel();
        return Task.FromResult(channel.MessageCount(queue));
    }

    private ServiceProvider BuildTrigger(string queue, string serverName, int port)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNexJob();
        services.AddNexJobRabbitMqTrigger(options =>
        {
            options.HostName = _fixture.HostName;
            options.Port = port;
            options.UserName = "guest";
            options.Password = "guest";
            options.QueueName = queue;
            options.ReconnectDelay = TimeSpan.FromSeconds(1);
            options.ConfigureConnection = factory => factory.Ssl = new SslOption
            {
                Enabled = true,
                ServerName = serverName,
                CertificateValidationCallback = TrustOnlyTheTestCa,
            };
        });
        return services.BuildServiceProvider();
    }

    private bool TrustOnlyTheTestCa(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (certificate is null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
        {
            return false;
        }

        using var custom = new X509Chain();
        custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        custom.ChainPolicy.CustomTrustStore.Add(_fixture.Ca);
        custom.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return custom.Build(new X509Certificate2(certificate));
    }

    private sealed class TestJob : IJob
    {
        public Task ExecuteAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
