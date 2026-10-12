using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Testcontainers.RabbitMq;
using Xunit;

namespace NexJob.Trigger.RabbitMQ.IntegrationTests;

/// <summary>
/// A RabbitMQ broker with a TLS listener whose certificate is signed by a CA generated for the test run.
/// </summary>
public sealed class RabbitMqTlsFixture : IAsyncLifetime
{
    private const string TlsConfig = """
        listeners.ssl.default = 5671
        ssl_options.cacertfile = /certs/ca.pem
        ssl_options.certfile = /certs/server.pem
        ssl_options.keyfile = /certs/server.key
        ssl_options.verify = verify_none
        ssl_options.fail_if_no_peer_cert = false
        """;

    private readonly X509Certificate2 _ca;
    private readonly RabbitMqContainer _container;

    /// <summary>Initializes a new instance of the <see cref="RabbitMqTlsFixture"/> class.</summary>
    public RabbitMqTlsFixture()
    {
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddDays(1);
        using var caKey = RSA.Create(2048);
        var caRequest = new CertificateRequest("CN=NexJob test CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        _ca = caRequest.CreateSelfSigned(notBefore, notAfter);

        using var serverKey = RSA.Create(2048);
        var serverRequest = new CertificateRequest("CN=localhost", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        serverRequest.CertificateExtensions.Add(names.Build());
        serverRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        using var server = serverRequest.Create(_ca, notBefore, notAfter, RandomNumberGenerator.GetBytes(8));

        _container = new RabbitMqBuilder()
            .WithImage("rabbitmq:3-management")
            .WithUsername("guest")
            .WithPassword("guest")
            .WithPortBinding(5671, true)
            .WithResourceMapping(Encoding.ASCII.GetBytes(_ca.ExportCertificatePem()), "/certs/ca.pem")
            .WithResourceMapping(Encoding.ASCII.GetBytes(server.ExportCertificatePem()), "/certs/server.pem")
            .WithResourceMapping(Encoding.ASCII.GetBytes(serverKey.ExportPkcs8PrivateKeyPem()), "/certs/server.key")
            .WithResourceMapping(Encoding.ASCII.GetBytes(TlsConfig), "/etc/rabbitmq/conf.d/20-tls.conf")
            .Build();
    }

    /// <summary>Gets the RabbitMQ host name.</summary>
    public string HostName => _container.Hostname;

    /// <summary>Gets the public port of the plain AMQP listener.</summary>
    public int PlainPort => _container.GetMappedPublicPort(5672);

    /// <summary>Gets the public port of the TLS listener.</summary>
    public int TlsPort => _container.GetMappedPublicPort(5671);

    /// <summary>Gets the CA that signed the broker's certificate.</summary>
    public X509Certificate2 Ca => _ca;

    /// <inheritdoc/>
    public Task InitializeAsync() => _container.StartAsync();

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
        _ca.Dispose();
    }
}
